using Avalonia.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Avalonia.Media.Imaging;

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
        private static readonly byte[,] _pendingCellValues = new byte[Rows, Cols];

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
        private const int Cols = 28;

        // Puffer für Bildzellen



        private static readonly byte[] _snapshotBuffer = new byte[Rows * Cols];


        private CancellationTokenSource _cts;


        public static TerminalManager terminalInstance { get; } = new TerminalManager();





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
                while (!_cts.Token.IsCancellationRequested)
                {
                    // Terminalbefehl an Steuerung
                    SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03 });
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

                lock (_pendingLock)
                {
                    for (int row = 1; row <= Rows; row++)
                    {
                        for (int col = 1; col <= Cols; col++)
                        {
                            int index = (row - 1) * Cols + (col - 1);
                            int srcIndex = 6 + index;
                            if (srcIndex >= response.Length)
                                continue;

                            byte newValue = response[srcIndex];

                            if (_pendingCellValues[row - 1, col - 1] != newValue)
                            {
                                _pendingCellValues[row - 1, col - 1] = newValue;
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

                int position = response[7];
                int newRow = position / Cols + 1;
                int newCol = (position % Cols) + 1;

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

            lock (_pendingLock)
            {
                doCells = _pendingCellsDirty;
                doCursor = _pendingCursorDirty;

                if (doCells)
                {
                    Buffer.BlockCopy(_pendingCellValues, 0, _snapshotBuffer, 0, Rows * Cols * sizeof(byte));
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
                long applied = ApplyCellsSnapshot_OnUiThread(_snapshotBuffer);
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
            if (newRow < 1 || newRow > Rows || newCol < 1 || newCol > Cols)
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


        private static long ApplyCellsSnapshot_OnUiThread(byte[] snapshot)
        {
            if (!terminalActive || Terminal.Instance == null)
                return 0;

            long updated = 0;

            int i = 0;
            for (int row = 1; row <= Rows; row++)
            {
                for (int col = 1; col <= Cols; col++)
                {
                    byte value = snapshot[i++];
                    var bmp = GetAsciiBitmap(value);
                    Terminal.Instance.UpdateCellImage(row, col, bmp);
                    updated++;
                }
            }

            return updated;
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



    }
}
