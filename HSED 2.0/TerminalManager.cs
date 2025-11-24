using Avalonia.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace HSED_2_0
{
    public class TerminalManager
    {
        // Aktuelle Cursorposition
        private static int _cursorRow = -1;
        private static int _cursorCol = -1;

        public static bool terminalActive { get; set; }

        // Letzte blinkende Position
        private static int _lastBlinkRow = -1;
        private static int _lastBlinkCol = -1;

        // Blinkstatus
        private static bool _blinkState = false;

        // Timer
        private static DispatcherTimer _blinkTimer;
        private static DispatcherTimer _uiUpdateTimer;

        // WICHTIG: 4 Zeilen x 28 Spalten
        private const int Rows = 4;
        private const int Cols = 28;

        // Puffer für Bildzellen
        private static readonly byte[,] _pendingCellValues = new byte[Rows, Cols];
        private static readonly bool[,] _cellDirty = new bool[Rows, Cols];
        private static bool _hasDirtyCells = false;

        // Cursor Puffer
        private static bool _cursorDirty = false;
        private static int _pendingCursorRow = -1;
        private static int _pendingCursorCol = -1;

        private CancellationTokenSource _cts;

        public static TerminalManager terminalInstance { get; } = new TerminalManager();

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
            ClearPending();
        }

        public void Close()
        {
            Stop();

            if (_blinkTimer != null)
            {
                _blinkTimer.Stop();
                _blinkTimer = null;
            }

            if (_uiUpdateTimer != null)
            {
                _uiUpdateTimer.Stop();
                _uiUpdateTimer = null;
            }
        }

        /// <summary>
        /// Analysiert die empfangene Response.
        /// 0x01 0x04  zeigt die Display Daten
        /// 0x01 0x02  zeigt die Cursorposition
        /// </summary>
        public static void AnalyzeResponse(byte[] response)
        {
            if (!terminalActive)
            {
                return;
            }

            // Displaydaten wie im alten funktionierenden Code, aber gepuffert
            if (response.Length >= 70 && response[4] == 0x01 && response[5] == 0x04)
            {
                for (int row = 1; row <= Rows; row++)
                {
                    for (int col = 1; col <= Cols; col++)
                    {
                        int index = (row - 1) * Cols + (col - 1);
                        if (6 + index < response.Length)
                        {
                            _pendingCellValues[row - 1, col - 1] = response[6 + index];
                            _cellDirty[row - 1, col - 1] = true;
                            _hasDirtyCells = true;
                        }
                    }
                }
                EnsureUiUpdateTimer();
            }

            // Cursor Nachricht
            if (response.Length >= 8 && response[4] == 0x01 && response[5] == 0x02)
            {
                int position = response[7];
                _pendingCursorRow = position / Cols + 1;  // gleiche Logik wie für Index
                _pendingCursorCol = position % Cols;      // hier nullbasiert im Protokoll
                _cursorDirty = true;
                EnsureUiUpdateTimer();
            }
        }

        /// <summary>
        /// Blink Timer für Cursor
        /// </summary>
        private static void StartBlinkTimer()
        {
            if (_blinkTimer == null)
            {
                _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _blinkTimer.Tick += (s, e) =>
                {
                    // Prüfen ob Position noch gleich ist
                    if (_cursorRow != _lastBlinkRow || _cursorCol != _lastBlinkCol)
                    {
                        Terminal.Instance?.UpdateCusorImage(_lastBlinkRow, _lastBlinkCol, null);
                        _blinkTimer.Stop();
                        _blinkState = false;
                        return;
                    }

                    _blinkState = !_blinkState;
                    if (_blinkState)
                    {
                        try
                        {
                            var bmp = AsciiLoader.LoadAsciiBitmap(0xFF);
                            Terminal.Instance?.UpdateCusorImage(_cursorRow, _cursorCol, bmp);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error in blink timer (showing cursor): {ex.Message}");
                        }
                    }
                    else
                    {
                        Terminal.Instance?.UpdateCusorImage(_cursorRow, _cursorCol, null);
                    }
                };
            }

            if (!_blinkTimer.IsEnabled)
            {
                _blinkTimer.Start();
            }
        }

        private static void EnsureUiUpdateTimer()
        {
            if (_uiUpdateTimer == null)
            {
                _uiUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _uiUpdateTimer.Tick += (s, e) =>
                {
                    if (!terminalActive)
                    {
                        ClearPending();
                        return;
                    }

                    if (_cursorDirty)
                    {
                        ApplyCursorUpdate();
                    }

                    if (_hasDirtyCells)
                    {
                        ApplyCellUpdates();
                    }
                };
            }

            if (!_uiUpdateTimer.IsEnabled)
            {
                _uiUpdateTimer.Start();
            }
        }

        private static void ApplyCursorUpdate()
        {
            _cursorDirty = false;

            Dispatcher.UIThread.Post(() =>
            {
                if (!terminalActive || Terminal.Instance == null)
                {
                    return;
                }

                int newRow = _pendingCursorRow;
                int newCol = _pendingCursorCol;
                Debug.WriteLine($"Neue Cursorposition: Zeile {newRow}, Spalte {newCol}");

                if (_lastBlinkRow != -1 && _lastBlinkCol != -1 &&
                    (newRow != _lastBlinkRow || newCol != _lastBlinkCol))
                {
                    Terminal.Instance.UpdateCusorImage(_lastBlinkRow, _lastBlinkCol, null);
                }

                _cursorRow = newRow;
                _cursorCol = newCol;
                _lastBlinkRow = newRow;
                _lastBlinkCol = newCol;

                try
                {
                    var cursorBmp = AsciiLoader.LoadAsciiBitmap(0xFF);
                    Terminal.Instance.UpdateCusorImage(_cursorRow, _cursorCol, cursorBmp);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading cursor bitmap: {ex.Message}");
                }

                StartBlinkTimer();
            });
        }

        private static void ApplyCellUpdates()
        {
            _hasDirtyCells = false;

            Dispatcher.UIThread.Post(() =>
            {
                if (!terminalActive || Terminal.Instance == null)
                {
                    return;
                }

                for (int row = 1; row <= Rows; row++)
                {
                    for (int col = 1; col <= Cols; col++)
                    {
                        if (_cellDirty[row - 1, col - 1])
                        {
                            _cellDirty[row - 1, col - 1] = false;
                            byte value = _pendingCellValues[row - 1, col - 1];
                            try
                            {
                                var bmp = AsciiLoader.LoadAsciiBitmap(value);
                                Terminal.Instance.UpdateCellImage(row, col, bmp);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error updating cell {row},{col}: {ex.Message}");
                            }
                        }
                    }
                }
            });
        }

        private static void ClearPending()
        {
            Array.Clear(_pendingCellValues, 0, _pendingCellValues.Length);
            Array.Clear(_cellDirty, 0, _cellDirty.Length);
            _hasDirtyCells = false;
            _cursorDirty = false;
            _pendingCursorRow = -1;
            _pendingCursorCol = -1;

            if (_blinkTimer != null && _blinkTimer.IsEnabled)
            {
                _blinkTimer.Stop();
            }

            if (_uiUpdateTimer != null && _uiUpdateTimer.IsEnabled)
            {
                _uiUpdateTimer.Stop();
            }
        }
    }
}
