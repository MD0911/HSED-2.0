using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using HSED_2_0.ViewModels;
using HSED_2._0;

namespace HSED_2_0
{
    public class MonetoringManager
    {
        // Statische Parameter für Etagenwerte:
        public static int BootFloor { get; private set; }
        public static int TopFloor { get; private set; }
        public static int GesamtFloor { get; private set; }
        public static int CurrentFloor { get; private set; } // umgerechneter Floor: raw + BootFloor - 1

        private CancellationTokenSource _cts;

        /// <summary>
        /// Startet den periodischen Monitoring-Abfragevorgang.
        /// </summary>
        public void Start()
        {
            _cts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    // Sende Monitoring-Befehl (z. B. 0x05,0x01) ohne auf Antwort zu warten.
                    SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x05, 0x01 });
                    try
                    {
                        await Task.Delay(1000, _cts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, _cts.Token);
        }

        /// <summary>
        /// Stoppt den Monitoring-Vorgang.
        /// </summary>
        public void Stop() => _cts?.Cancel();

        /// <summary>
        /// Initialisiert einmalig BootFloor und TopFloor und berechnet GesamtFloor.
        /// Diese Methode MUSS beim Start des Monitoring-Prozesses aufgerufen werden.
        /// </summary>
        public static void startMonetoring()
        {
            byte[] bottomfloorResponse = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
            byte[] topfloorResponse = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x01, 0x01, 0x03 });

            if (bottomfloorResponse == null || bottomfloorResponse.Length <= 11 ||
                topfloorResponse == null || topfloorResponse.Length <= 11)
            {
                Debug.WriteLine("Fehler beim Abfragen von BootFloor/TopFloor.");
                return;
            }

            // Annahme: TopFloor-Wert steht an Position 10
            TopFloor = topfloorResponse[10];
            Debug.WriteLine("TopFloor (Rohwert): " + TopFloor);

            // BootFloor: Bytes an Position 11 und 10 als ASCII
            byte[] bottomfloorName = new byte[2];
            bottomfloorName[0] = bottomfloorResponse[11];
            bottomfloorName[1] = bottomfloorResponse[10];
            string asciiString = Encoding.ASCII.GetString(bottomfloorName);
            Debug.WriteLine("ASCII BootFloor: " + asciiString);
            try
            {
                BootFloor = Convert.ToInt32(asciiString);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Fehler bei der Umrechnung des BootFloor: " + ex.Message);
                BootFloor = 0;
            }

            GesamtFloor = (TopFloor - BootFloor) + 1;

            Debug.WriteLine("BootFloor: " + BootFloor);
            Debug.WriteLine("TopFloor: " + TopFloor);
            Debug.WriteLine("GesamtFloor: " + GesamtFloor);
        }

        /// <summary>
        /// Aktualisiert CurrentFloor anhand eines Monitoring-Telegramms (angenommen ab Offset 10).
        /// Umrechnung: CurrentFloor = rawFloor + BootFloor - 1.
        /// </summary>
        private static void setCurrentFloor(byte[] currentFloorResponse)
        {
            if (currentFloorResponse == null || currentFloorResponse.Length <= 10)
            {
                Debug.WriteLine("Ungültige Antwort für CurrentFloor.");
                return;
            }

            int rawFloor = currentFloorResponse[10];
            Debug.WriteLine("Rohwert (rawFloor) an Offset 10: " + rawFloor);
            CurrentFloor = rawFloor + BootFloor;
            Debug.WriteLine($"setCurrentFloor: raw = {rawFloor}, BootFloor = {BootFloor}, CurrentFloor = {CurrentFloor}");

            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.CurrentFloor = CurrentFloor;
                }
            });
        }

        /// <summary>
        /// Analysiert empfangene Telegramme.
        /// Bei einem Monitoring-Telegramm (0x05,0x02) wird unterschieden:
        ///  - Zustandsindex 0x2101: Floor (wird via setCurrentFloor verarbeitet)
        ///  - Zustandsindex 0x2102: SK-Zustand (wird ins ViewModel geschrieben)
        /// </summary>
        /// 
        /*public static void animationValidator()
        {
            HseCom.SendHseCommand(new byte[] { 0x03, 0x01,  });

        }*/
        public static void AnalyzeResponse(byte[] response)
        {
            Debug.WriteLine("Derzeitige Etage (vor Analyse): " + CurrentFloor);
            if (response == null || response.Length < 8)
            {
                Debug.WriteLine("Response zu kurz.");
                return;
            }

            if (response[4] == 0x05 && response[5] == 0x02)
            {
                // Floor-Daten (Zustandsindex 0x2101)
                if (response[6] == 0x21 && response[7] == 0x01)
                {
                    Debug.WriteLine("Etagenänderung erkannt.");
                    setCurrentFloor(response);
                }
                // SK-Daten (Zustandsindex 0x2102)
                else if (response[6] == 0x21 && response[7] == 0x02)
                {
                    Debug.WriteLine("SK-Zustand erkannt.");
                    int skValue = response[10]; // Annahme: SK-Wert an Position 10
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (MainWindow.Instance?.ViewModel != null)
                        {
                            MainWindow.Instance.ViewModel.SKValue = skValue;
                        }
                    });
                }
                // Weitere Zustände (z. B. A-Zustand) können hier analog verarbeitet werden.
            }
            else
            {
                Debug.WriteLine($"Telegrammart ist nicht 0x0502, sondern {response[4]:X2} {response[5]:X2}");
            }
        }
    }
}
