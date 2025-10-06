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
        public static bool terminalActive { get; set; }
        private static int _cursorCol = -1;
        // Letzte blinkende Position (wird vom Timer verwendet, um zu prüfen, ob sich die Position geändert hat)
        private static int _lastBlinkRow = -1;
        private static int _lastBlinkCol = -1;
        // Aktueller Blinkstatus (true = Cursor-Bitmap anzeigen, false = löschen)
        private static bool _blinkState = false;
        // Unser Timer zum Togglen des Cursors
        private static DispatcherTimer _blinkTimer;
        private CancellationTokenSource _cts;
        
        public static object Instance { get; internal set; }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            // Hier simulieren wir den Sendezyklus – in der echten Anwendung wird hier der entsprechende Befehl gesendet
            Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    // Sende beispielsweise den Terminalbefehl (dieser liefert auch Cursor-Nachrichten)
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

        public void Stop() => _cts?.Cancel();
       

        /// <summary>
        /// Analysiert die empfangene Response. Bei (0x01,0x04) werden Bildzellen aktualisiert,
        /// bei (0x01,0x02) wird die Cursorposition verarbeitet.
        /// </summary>
        public static void AnalyzeResponse(byte[] response)
        {
            if (terminalActive)
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

               
            }
        }
    }
}

        /// <summary>
        /// Startet oder setzt den Blink-Timer neu. Der Timer toggelt alle 500ms den Cursor.
        /// Vor jedem Toggle prüft er, ob die aktuell gespeicherte Cursorposition (_cursorRow/_cursorCol)
        /// noch der zuletzt blinkenden Position entspricht (_lastBlinkRow/_lastBlinkCol). Falls nicht,
        /// wird der Timer zurückgesetzt.
        /// </summary>
       
