using Avalonia.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using System.Runtime.CompilerServices;

namespace HSED_2_0
{
    public class TerminalManager
    {

        // Lock für Pending State
        private static readonly object _pendingLock = new object();

        // Dirty Flags
        private static bool _pendingCellsDirty = false;
        private static bool _pendingCursorDirty = false;

        // Pending Cursor
        private static int _pendingCursorRow = -1;
        private static int _pendingCursorCol = -1;

        // Pending Cells
        private static readonly byte[,] _pendingCellValues = new byte[Rows, MaxCols];

        // UI Flush Timer
        private static DispatcherTimer? _uiFlushTimer;

        // Ziel FPS für Terminal UI
        private const int UiFlushIntervalMs = 33; // 30 FPS

        // Aktuelle Cursorposition
        private static int _cursorRow = -1;
        private static int _cursorCol = -1;

        public static bool terminalActive { get; set; }

        // Klasse: TerminalManager
        private static int _blinkRow = -1;
        private static int _blinkCol = -1;

        // Letzte blinkende Position
        private static int _lastBlinkRow = -1;
        private static int _lastBlinkCol = -1;

        // Blinkstatus
        private static bool _blinkState = false;

        // Timer
        private static DispatcherTimer _blinkTimer;

        // WICHTIG: 4 Zeilen x 28 Spalten
        private const int Rows = 4;
        private const int DefaultCols = 16;
        private const int MaxCols = 35;

        // Puffer für Bildzellen



        private static readonly byte[] _snapshotBuffer = new byte[Rows * MaxCols];

        private static int _currentCols = DefaultCols;
        private static int _currentLine = 1;
        private static int _currentLineSelectorRaw = 0;
        private static int _pendingColumns = DefaultCols;


        private CancellationTokenSource _cts;


        public static TerminalManager terminalInstance { get; } = new TerminalManager();

        public static int CurrentColumns => Volatile.Read(ref _currentCols);
        public static int CurrentLine => Volatile.Read(ref _currentLine);
        public static int CurrentLineSelectorRaw => Volatile.Read(ref _currentLineSelectorRaw);





        // Temp for Logging

        // Klasse: TerminalManager (Felder)
        private static long _rxTotal = 0;
        private static long _rxDisplay = 0;
        private static long _rxCursor = 0;
        private static long _rxDroppedAnalyzing = 0;

        private static long _uiRuns = 0;
        private static long _uiCursorRuns = 0;
        private static long _uiCellRuns = 0;

        private static long _cellUpdatesApplied = 0;
        private static long _maxUiRunMs = 0;

        private static readonly Stopwatch _logSw = Stopwatch.StartNew();
        private static long _lastLogMs = 0;
        private static long _uiRunTotalMs = 0;
        private static long _uiRunCount = 0;

        // Klasse: TerminalManager
        public static int SimulatedUiCostMs { get; set; } = 0;


        // Wenn true, wird geloggt. Auf dem Pi später ggf. false setzen.
        public static bool PerfLoggingEnabled { get; set; } = false;





        public void Start()
        {
            terminalActive = true;
            _cts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                await InitializeTerminalConfigurationAsync(_cts.Token);

                if (_cts.Token.IsCancellationRequested)
                    return;

                while (!_cts.Token.IsCancellationRequested)
                {
                    // Terminalbefehl an Steuerung
                    await SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03 });
                    try
                    {
                        await Task.Delay(1900, _cts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, _cts.Token);
        }

        public void Stop()
        {
            terminalActive = false;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            StopUiFlushTimer();
            ClearPending();
        }


        // Klasse: TerminalManager
        public void Close()
        {
            Stop();

            if (_blinkTimer != null)
            {
                _blinkTimer.Stop();
                _blinkTimer = null;
            }

            // _uiUpdateTimer gibt es nicht mehr
        }


        /// <summary>
        /// Analysiert die empfangene Response.
        /// 0x01 0x04  zeigt die Display Daten
        /// 0x01 0x02  zeigt die Cursorposition
        /// </summary>
        // Klasse: TerminalManager
        // Klasse: TerminalManager
        // Klasse: TerminalManager
        // Klasse: TerminalManager
        private static int _ensureTimerPosted = 0;
        public static void AnalyzeResponse(byte[] response)
        {
            if (!terminalActive)
                return;

            Interlocked.Increment(ref _rxTotal);

            bool anyChange = false;

            if (response.Length >= 70 && response[4] == 0x01 && response[5] == 0x04)
            {
                Interlocked.Increment(ref _rxDisplay);

                int availableCells = Math.Max(0, response.Length - 8);
                int activeCols = CurrentColumns;
                int cols = InferColumnsFromDisplayPayloadLength(availableCells, activeCols);
                int expectedCells = Rows * cols;

                if (cols >= 28 || availableCells != expectedCells || cols != activeCols)
                {
                    Debug.WriteLine(
                        $"[Terminal][Display] Telegramm empfangen: Laenge={response.Length}, Nutzdaten={availableCells}, erwartet={expectedCells}, Spalten={cols}, aktiv={activeCols}");
                }

                lock (_pendingLock)
                {
                    _pendingColumns = cols;

                    for (int row = 1; row <= Rows; row++)
                    {
                        for (int col = 1; col <= cols; col++)
                        {
                            int index = (row - 1) * cols + (col - 1);
                            int srcIndex = 6 + index;
                            byte newValue = srcIndex < response.Length - 2 ? response[srcIndex] : (byte)0x00;

                            if (_pendingCellValues[row - 1, col - 1] != newValue)
                            {
                                _pendingCellValues[row - 1, col - 1] = newValue;
                                anyChange = true;
                            }
                        }

                        for (int col = cols + 1; col <= MaxCols; col++)
                        {
                            if (_pendingCellValues[row - 1, col - 1] != 0x00)
                            {
                                _pendingCellValues[row - 1, col - 1] = 0x00;
                                anyChange = true;
                            }
                        }
                    }

                    if (anyChange)
                        _pendingCellsDirty = true;
                }
            }

            if (response.Length >= 8 && response[4] == 0x01 && response[5] == 0x02)
            {
                Interlocked.Increment(ref _rxCursor);

                int position = Math.Max(0, response[7] - 1);
                int cols = CurrentColumns;
                int newRow = position / cols + 1;
                int newCol = (position % cols) + 1;

                lock (_pendingLock)
                {
                    if (newRow != _pendingCursorRow || newCol != _pendingCursorCol)
                    {
                        _pendingCursorRow = newRow;
                        _pendingCursorCol = newCol;
                        _pendingCursorDirty = true;
                    }
                }
            }

            // Nur starten, wenn Terminal da ist und was dirty ist
            if (Terminal.Instance == null)
                return;

            bool needFlush;
            lock (_pendingLock)
            {
                needFlush = _pendingCellsDirty || _pendingCursorDirty;
            }
            if (!needFlush || _uiFlushTimer != null)
                return;

            // Flag erst hier setzen, damit es nicht "hängen bleibt"
            if (Interlocked.Exchange(ref _ensureTimerPosted, 1) == 1)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_uiFlushTimer == null && terminalActive && Terminal.Instance != null)
                        EnsureUiFlushTimerStarted();
                }
                finally
                {
                    Volatile.Write(ref _ensureTimerPosted, 0);
                }
            });
        }



        private static void FlushPendingToUi_OnUiThread()
        {
            if (!terminalActive || Terminal.Instance == null)
                return;

            bool doCells;
            bool doCursor;

            int curRow = -1;
            int curCol = -1;
            int snapshotCols = CurrentColumns;

            lock (_pendingLock)
            {
                doCells = _pendingCellsDirty;
                doCursor = _pendingCursorDirty;

                if (doCells)
                {
                    Buffer.BlockCopy(_pendingCellValues, 0, _snapshotBuffer, 0, Rows * MaxCols * sizeof(byte));
                    snapshotCols = _pendingColumns;
                    _pendingCellsDirty = false;
                }

                if (doCursor)
                {
                    curRow = _pendingCursorRow;
                    curCol = _pendingCursorCol;
                    _pendingCursorDirty = false;
                }
            }

            // Wenn aktuell nichts zu tun ist, Timer stoppen damit kein Dauer Tick auf dem Pi läuft
            if (!doCells && !doCursor)
            {
                StopUiFlushTimer();
                return;
            }

            var sw = Stopwatch.StartNew();

            Interlocked.Increment(ref _uiRuns);

            if (doCursor)
            {
                ApplyCursorSnapshot_OnUiThread(curRow, curCol);
                Interlocked.Increment(ref _uiCursorRuns);
            }

            if (doCells)
            {
                long applied = ApplyCellsSnapshot_OnUiThread(_snapshotBuffer, snapshotCols);
                Interlocked.Increment(ref _uiCellRuns);
                Interlocked.Add(ref _cellUpdatesApplied, applied);
            }

            sw.Stop();
            long ms = sw.ElapsedMilliseconds;

            long prevMax = Volatile.Read(ref _maxUiRunMs);
            if (ms > prevMax)
                Volatile.Write(ref _maxUiRunMs, ms);

            Interlocked.Add(ref _uiRunTotalMs, ms);
            Interlocked.Increment(ref _uiRunCount);
        }



        private static void ApplyCursorSnapshot_OnUiThread(int newRow, int newCol)
        {
            if (!terminalActive || Terminal.Instance == null)
                return;

            // FIX: Bounds jetzt 1..Rows und 1..Cols
            if (newRow < 1 || newRow > Rows || newCol < 1 || newCol > CurrentColumns)
                return;

            // Alten Cursor löschen
            if (_lastBlinkRow != -1 && _lastBlinkCol != -1 &&
                (newRow != _lastBlinkRow || newCol != _lastBlinkCol))
            {
                Terminal.Instance.UpdateCusorImage(_lastBlinkRow, _lastBlinkCol, null);
            }

            _cursorRow = newRow;
            _cursorCol = newCol;

            var cursorBmp = GetAsciiBitmap(0xFF);
            Terminal.Instance.UpdateCusorImage(_cursorRow, _cursorCol, cursorBmp);

            _lastBlinkRow = _cursorRow;
            _lastBlinkCol = _cursorCol;

            StartBlinkTimer();
        }


        private static long ApplyCellsSnapshot_OnUiThread(byte[] snapshot, int cols)
        {
            if (!terminalActive || Terminal.Instance == null)
                return 0;

            long updated = 0;
            int normalizedCols = NormalizeColumns(cols);

            Terminal.Instance.SetColumnCount(normalizedCols);

            for (int row = 1; row <= Rows; row++)
            {
                int rowOffset = (row - 1) * MaxCols;
                for (int col = 1; col <= normalizedCols; col++)
                {
                    byte value = snapshot[rowOffset + (col - 1)];
                    var bmp = GetAsciiBitmap(value);
                    Terminal.Instance.UpdateCellImage(row, col, bmp);
                    updated++;
                }
            }

            return updated;
        }

        private static int InferColumnsFromDisplayPayloadLength(int availableCells, int fallbackCols)
        {
            return availableCells switch
            {
                64 => 16,
                104 => 26,
                112 => 28,
                140 => 35,
                _ => NormalizeColumns(availableCells >= Rows ? availableCells / Rows : fallbackCols)
            };
        }




        // Klasse: TerminalManager
        private static readonly Bitmap[] _asciiCache = new Bitmap[256];

        private static Bitmap GetAsciiBitmap(byte value)
        {
            var bmp = _asciiCache[value];
            if (bmp != null)
                return bmp;

            bmp = AsciiLoader.LoadAsciiBitmap(value);
            _asciiCache[value] = bmp;
            return bmp;
        }

        private static void EnsureUiFlushTimerStarted()
        {
            // Safety: nur wenn Terminal wirklich aktiv und UI da ist
            if (!terminalActive || Terminal.Instance == null)
                return;

            if (_uiFlushTimer != null)
                return;

            _uiFlushTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(UiFlushIntervalMs)
            };

            _uiFlushTimer.Tick += (_, __) =>
            {
                FlushPendingToUi_OnUiThread();
                LogPerfIfNeeded();
            };

            _uiFlushTimer.Start();
        }



        private static void StopUiFlushTimer()
        {
            var t = _uiFlushTimer;
            if (t == null)
                return;

            _uiFlushTimer = null;

            // DispatcherTimer sauber auf UI-Thread stoppen
            if (Dispatcher.UIThread.CheckAccess())
            {
                t.Stop();
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try { t.Stop(); } catch { }
                });
            }
        }







        /// <summary>
        /// Blink Timer für Cursor
        /// </summary>
        // Klasse: TerminalManager
        // Klasse: TerminalManager
        private static void StartBlinkTimer()
        {
            // Merke die Position, für die dieser Blinklauf gilt
            _blinkRow = _cursorRow;
            _blinkCol = _cursorCol;

            if (_blinkTimer == null)
            {
                _blinkTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };

                _blinkTimer.Tick += (s, e) =>
                {
                    // WICHTIG: Terminal nicht aktiv oder UI weg → sofort stoppen
                    if (!terminalActive || Terminal.Instance == null)
                    {
                        _blinkTimer.Stop();
                        _blinkState = false;
                        return;
                    }

                    // Wenn Cursor sich verschoben hat: alten Cursor ausblenden und Timer stoppen
                    if (_cursorRow != _blinkRow || _cursorCol != _blinkCol)
                    {
                        Terminal.Instance.UpdateCusorImage(_blinkRow, _blinkCol, null);
                        _blinkTimer.Stop();
                        _blinkState = false;
                        return;
                    }

                    _blinkState = !_blinkState;

                    if (_blinkState)
                    {
                        var bmp = GetAsciiBitmap(0xFF);
                        Terminal.Instance.UpdateCusorImage(_cursorRow, _cursorCol, bmp);
                    }
                    else
                    {
                        Terminal.Instance.UpdateCusorImage(_cursorRow, _cursorCol, null);
                    }
                };
            }

            if (!_blinkTimer.IsEnabled)
            {
                _blinkTimer.Start();
            }
        }




        // Klasse: TerminalManager
        // Klasse: TerminalManager



        // Klasse: TerminalManager
        // Klasse: TerminalManager
        // Klasse: TerminalManager



        // Klasse: TerminalManager
        // Klasse: TerminalManager
        private static void LogPerfIfNeeded()
        {
            if (!PerfLoggingEnabled)
                return;

            long now = _logSw.ElapsedMilliseconds;
            long last = Volatile.Read(ref _lastLogMs);

            if (now - last < 2000)
                return;

            if (Interlocked.CompareExchange(ref _lastLogMs, now, last) != last)
                return;

            long rxTotal = Volatile.Read(ref _rxTotal);
            long rxDisplay = Volatile.Read(ref _rxDisplay);
            long rxCursor = Volatile.Read(ref _rxCursor);
            long dropped = Volatile.Read(ref _rxDroppedAnalyzing);

            long uiRuns = Volatile.Read(ref _uiRuns);
            long uiCursorRuns = Volatile.Read(ref _uiCursorRuns);
            long uiCellRuns = Volatile.Read(ref _uiCellRuns);

            long cellApplied = Volatile.Read(ref _cellUpdatesApplied);

            long maxUiMsWindow = Interlocked.Exchange(ref _maxUiRunMs, 0);
            long totalMsWindow = Interlocked.Exchange(ref _uiRunTotalMs, 0);
            long countWindow = Interlocked.Exchange(ref _uiRunCount, 0);

            long avgUiMsWindow = countWindow > 0 ? (totalMsWindow / countWindow) : 0;

            Debug.WriteLine(
                $"[PERF] RX total={rxTotal}, disp={rxDisplay}, cur={rxCursor}, dropped(Analyze)={dropped} | " +
                $"UI runs={uiRuns} (curRuns={uiCursorRuns}, cellRuns={uiCellRuns}) | " +
                $"cellsApplied={cellApplied} | avgUIRun={avgUiMsWindow}ms | maxUIRun={maxUiMsWindow}ms | runsWindow={countWindow}"
            );
        }








        // Klasse: TerminalManager
        // Klasse: TerminalManager






        // Klasse: TerminalManager
        private static void ClearPending()
        {
            StopUiFlushTimer();

            lock (_pendingLock)
            {
                Array.Clear(_pendingCellValues, 0, _pendingCellValues.Length);
                _pendingCellsDirty = false;

                _pendingCursorDirty = false;
                _pendingCursorRow = -1;
                _pendingCursorCol = -1;

                // Snapshot-Buffer ebenfalls zurücksetzen
                Array.Clear(_snapshotBuffer, 0, _snapshotBuffer.Length);
            }

            if (_blinkTimer != null && _blinkTimer.IsEnabled)
                _blinkTimer.Stop();

            _cursorRow = -1;
            _cursorCol = -1;
            _lastBlinkRow = -1;
            _lastBlinkCol = -1;
            _blinkState = false;
        }

        public static void ApplyTerminalCharacterWidthFromMonitoring(int line, int columns)
        {
            if (line != CurrentLineSelectorRaw && line != CurrentLine)
                return;

            ApplyTerminalConfiguration(line, columns);
        }

        public static void RequestColumnsForCurrentTerminalMode(bool zoomEnabled)
        {
            int desiredColumns = zoomEnabled ? 35 : 16;

            Task.Run(() =>
            {
                try
                {
                    int rawLineSelector = CurrentLineSelectorRaw;
                    if (rawLineSelector < 0 || rawLineSelector > 2)
                        rawLineSelector = HseCom.ReadTerminalLine();

                    if (rawLineSelector < 0 || rawLineSelector > 2)
                    {
                        Debug.WriteLine(
                            $"[Terminal] Konnte Leitung fuer Spaltenumschaltung nicht lesen. Gewuenscht={desiredColumns}");
                        return;
                    }

                    Debug.WriteLine(
                        $"[Terminal] Fordere {desiredColumns} Zeichen fuer Leitung raw={rawLineSelector}, angezeigt={NormalizeDisplayLine(rawLineSelector)} an");

                    bool writeSucceeded = HseCom.WriteTerminalColumnsForLine(rawLineSelector, desiredColumns);
                    if (!writeSucceeded)
                        return;

                    ApplyTerminalConfiguration(rawLineSelector, desiredColumns);
                    _ = SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03 });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Terminal] Fehler beim Umschalten der Zeichenzahl: {ex.Message}");
                }
            });
        }

        private async Task InitializeTerminalConfigurationAsync(CancellationToken token)
        {
            try
            {
                int rawLineSelector = await Task.Run(HseCom.ReadTerminalLine, token);
                if (token.IsCancellationRequested)
                    return;

                if (rawLineSelector < 0 || rawLineSelector > 2)
                {
                    await RefreshTerminalConfigurationAsync(token);
                    return;
                }

                Debug.WriteLine(
                    $"[Terminal] Startinitialisierung: setze 16 Zeichen fuer Leitung raw={rawLineSelector}, angezeigt={NormalizeDisplayLine(rawLineSelector)}");

                bool writeSucceeded = await Task.Run(() => HseCom.WriteTerminalColumnsForLine(rawLineSelector, DefaultCols), token);
                if (token.IsCancellationRequested)
                    return;

                if (writeSucceeded)
                {
                    ApplyTerminalConfiguration(rawLineSelector, DefaultCols);
                    return;
                }

                await RefreshTerminalConfigurationAsync(token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Terminal-Startkonfiguration konnte nicht gesetzt werden: " + ex.Message);
                await RefreshTerminalConfigurationAsync(token);
            }
        }

        private async Task RefreshTerminalConfigurationAsync(CancellationToken token)
        {
            try
            {
                int rawLineSelector = await Task.Run(HseCom.ReadTerminalLine, token);
                if (token.IsCancellationRequested)
                    return;

                if (rawLineSelector < 0 || rawLineSelector > 2)
                    rawLineSelector = CurrentLineSelectorRaw;

                Debug.WriteLine(
                    $"[Terminal] Erkannte Leitung: raw={rawLineSelector}, angezeigt={NormalizeDisplayLine(rawLineSelector)}");

                int columns = await Task.Run(() => HseCom.ReadTerminalColumnsForLine(rawLineSelector), token);
                if (token.IsCancellationRequested)
                    return;

                Debug.WriteLine(
                    $"[Terminal] Gelesene Zeichenbreite fuer Leitung raw={rawLineSelector}, angezeigt={NormalizeDisplayLine(rawLineSelector)}: {columns}");

                ApplyTerminalConfiguration(rawLineSelector, columns);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Terminal-Konfiguration konnte nicht gelesen werden: " + ex.Message);
            }
        }

        private static void ApplyTerminalConfiguration(int rawLineSelector, int columns)
        {
            int normalizedLine = NormalizeDisplayLine(rawLineSelector);
            int normalizedColumns = NormalizeColumns(columns);
            int previousLine = CurrentLine;
            int previousColumns = CurrentColumns;
            int previousRawLine = CurrentLineSelectorRaw;
            bool columnsChanged = previousColumns != normalizedColumns;

            bool changed = false;

            if (CurrentLineSelectorRaw != rawLineSelector)
            {
                Volatile.Write(ref _currentLineSelectorRaw, rawLineSelector);
                changed = true;
            }

            if (CurrentLine != normalizedLine)
            {
                Volatile.Write(ref _currentLine, normalizedLine);
                changed = true;
            }

            if (CurrentColumns != normalizedColumns)
            {
                Volatile.Write(ref _currentCols, normalizedColumns);
                changed = true;
            }

            if (!changed)
            {
                Debug.WriteLine(
                    $"[Terminal] Konfiguration unveraendert: Leitung raw={rawLineSelector}, angezeigt={normalizedLine}, {normalizedColumns} Spalten");
                return;
            }

            Debug.WriteLine(
                $"[Terminal] Konfiguration aktualisiert: Leitung raw {previousRawLine} -> {rawLineSelector}, " +
                $"angezeigt {previousLine} -> {normalizedLine}, " +
                $"Spalten {previousColumns} -> {normalizedColumns} (Rohwert: {columns})");

            lock (_pendingLock)
            {
                if (columnsChanged)
                {
                    Array.Clear(_pendingCellValues, 0, _pendingCellValues.Length);
                    Array.Clear(_snapshotBuffer, 0, _snapshotBuffer.Length);
                    _pendingColumns = normalizedColumns;
                    _pendingCursorRow = -1;
                    _pendingCursorCol = -1;
                }

                _pendingCellsDirty = true;
                _pendingCursorDirty = true;

                if (_pendingCursorCol > normalizedColumns)
                {
                    _pendingCursorRow = -1;
                    _pendingCursorCol = -1;
                }
            }

            if (Terminal.Instance != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Terminal.Instance.SetColumnCount(normalizedColumns);
                    if (columnsChanged)
                        Terminal.Instance.ClearDisplay();

                    EnsureUiFlushTimerStarted();
                });
            }

            if (columnsChanged)
            {
                _cursorRow = -1;
                _cursorCol = -1;
                _lastBlinkRow = -1;
                _lastBlinkCol = -1;
                _blinkRow = -1;
                _blinkCol = -1;

                _ = SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03 });
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NormalizeDisplayLine(int rawLineSelector)
        {
            return rawLineSelector switch
            {
                0 => 1,
                2 => 2,
                _ => 1
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NormalizeColumns(int columns)
        {
            return columns switch
            {
                28 => 28,
                26 => 26,
                35 => 35,
                _ => DefaultCols
            };
        }



    }
}
