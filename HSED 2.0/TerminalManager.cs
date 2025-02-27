using Avalonia.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace HSED_2_0
{
    public class TerminalManager
    {
        // Aktuelle Cursorposition (wird bei jeder Cursor-Nachricht aktualisiert)
        private static int _cursorRow = -1;
        private static int _cursorCol = -1;
        // Letzte blinkende Position (wird vom Timer verwendet, um zu prüfen, ob sich die Position geändert hat)
        private static int _lastBlinkRow = -1;
        private static int _lastBlinkCol = -1;
        // Aktueller Blinkstatus (true = Cursor-Bitmap anzeigen, false = löschen)
        private static bool _blinkState = false;
        // Unser Timer zum Togglen des Cursors
        private static DispatcherTimer _blinkTimer;
        private CancellationTokenSource _cts;

        public void Start()
        {
            _cts = new CancellationTokenSource();
            // Hier simulieren wir den Sendezyklus – in der echten Anwendung wird hier der entsprechende Befehl gesendet
            Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    // Sende beispielsweise den Terminalbefehl (dieser liefert auch Cursor-Nachrichten)
                    HseCom.SendHseCommand(new byte[] { 0x01, 0x03 });
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

        public void Stop() => _cts?.Cancel();

        /// <summary>
        /// Analysiert die empfangene Response. Bei (0x01,0x04) werden Bildzellen aktualisiert,
        /// bei (0x01,0x02) wird die Cursorposition verarbeitet.
        /// </summary>
        public static void AnalyzeResponse(byte[] response)
        {
            // Aktualisiere die Bildzellen (s. früheren Code)
            if (response.Length >= 70 && response[4] == 0x01 && response[5] == 0x04)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    for (int row = 1; row <= 4; row++)
                    {
                        for (int col = 1; col <= 16; col++)
                        {
                            int index = (row - 1) * 16 + (col - 1);
                            if (6 + index < response.Length)
                            {
                                byte value = response[6 + index];
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

            // Cursor-Nachricht
            if (response.Length >= 8 && response[4] == 0x01 && response[5] == 0x02)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    int position = response[7];
                    int newRow = position / 16 + 1;
                    int newCol = position % 16;
                    Debug.WriteLine($"Neue Cursorposition: Zeile {newRow}, Spalte {newCol}");

                    // Falls sich die Position ändert, lösche den Cursor in der alten Position
                    if (_lastBlinkRow != -1 && _lastBlinkCol != -1 &&
                        (newRow != _lastBlinkRow || newCol != _lastBlinkCol))
                    {
                        Terminal.Instance.UpdateCusorImage(_lastBlinkRow, _lastBlinkCol, null);
                    }
                    // Aktualisiere die aktuelle Cursorposition
                    _cursorRow = newRow;
                    _cursorCol = newCol;
                    // Setze die letzte blinkende Position auf die neue Position
                    _lastBlinkRow = newRow;
                    _lastBlinkCol = newCol;

                    // Zeige den Cursor sofort an (z.B. mit Bitmap 0xFF)
                    try
                    {
                        var cursorBmp = AsciiLoader.LoadAsciiBitmap(0xFF);
                        Terminal.Instance.UpdateCusorImage(_cursorRow, _cursorCol, cursorBmp);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error loading cursor bitmap: {ex.Message}");
                    }
                    // Starte den Blink-Timer
                    StartBlinkTimer();
                });
            }
        }

        /// <summary>
        /// Startet oder setzt den Blink-Timer neu. Der Timer toggelt alle 500ms den Cursor.
        /// Vor jedem Toggle prüft er, ob die aktuell gespeicherte Cursorposition (_cursorRow/_cursorCol)
        /// noch der zuletzt blinkenden Position entspricht (_lastBlinkRow/_lastBlinkCol). Falls nicht,
        /// wird der Timer zurückgesetzt.
        /// </summary>
        private static void StartBlinkTimer()
        {
            if (_blinkTimer == null)
            {
                _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _blinkTimer.Tick += (s, e) =>
                {
                    // Prüfe, ob sich die blinkende Position noch geändert hat
                    if (_cursorRow != _lastBlinkRow || _cursorCol != _lastBlinkCol)
                    {
                        // Position hat sich geändert – stoppe den Timer (er wird beim nächsten Cursor-Update wieder gestartet)
                        Terminal.Instance.UpdateCusorImage(_lastBlinkRow, _lastBlinkCol, null);
                        _blinkTimer.Stop();
                        _blinkState = false;
                        return;
                    }

                    // Toggle den Blinkstatus
                    _blinkState = !_blinkState;
                    if (_blinkState)
                    {
                        try
                        {
                            var bmp = AsciiLoader.LoadAsciiBitmap(0xFF); // Beispiel-Cursor-Bitmap
                            Terminal.Instance.UpdateCusorImage(_cursorRow, _cursorCol, bmp);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error in blink timer (showing cursor): {ex.Message}");
                        }
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
    }
}
