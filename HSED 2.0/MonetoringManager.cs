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
        public static int CurrentTemp { get; private set; }
        public static int CurrentSK1 { get; private set; }
        public static int CurrentSK2 { get; private set; }

        public static int Betriebsstunden { get; private set; }

        public static float LastKorbPosition { get; private set; }
        public static int CurrentSK3 { get; private set; }

        public static int CurrentSK4 { get; private set; }
        public static int CurrentLast { get; private set; }
        public static int CurrentZustand { get; private set; }

        




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
                   SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x05, 0x01, 0x01});
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
          

            int rawFloor = currentFloorResponse[4];
            Debug.WriteLine("Rohwert: " + rawFloor);
            Debug.WriteLine("BootFloor: " + BootFloor);
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

        private static void setTemp(byte[] temp)
        {
            

            int newTemp = temp[4];
            CurrentTemp = newTemp;
            
            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.CurrentTemp = CurrentTemp;
                }
            });
        }

        private static void setSK(byte[] SK)
        {
           

            byte sk = SK[4];
            bool[] Sk = new bool[4];
            for (int i = 0; i < 4; i++)
            {
                Sk[i] = (sk & (1 << i)) != 0;
            }
            CurrentSK1 = Sk[0] ? 1 : 0;
            CurrentSK2 = Sk[1] ? 1 : 0;
            CurrentSK3 = Sk[2] ? 1 : 0;
            CurrentSK4 = Sk[3] ? 1 : 0;
            
            Debug.WriteLine("SK1: " + CurrentSK1);
            Debug.WriteLine("SK2: " + CurrentSK2);
            Debug.WriteLine("SK3: " + CurrentSK3);
            Debug.WriteLine("SK4: " + CurrentSK4);

            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.CurrentSK1 = CurrentSK1;
                    MainWindow.Instance.ViewModel.CurrentSK2 = CurrentSK2;
                    MainWindow.Instance.ViewModel.CurrentSK3 = CurrentSK3;
                    MainWindow.Instance.ViewModel.CurrentSK4 = CurrentSK4;
                }
            });
        }

        private static void setLast(byte[] last)
        {


            int newLast = BitConverter.ToInt16(new byte[] { last[4], last[5] }, 0);
            Debug.WriteLine("Last: " + newLast);

            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.CurrentLast = newLast;
                }
            });
        }

        private static void setZustand(byte[] zustand)
        {
           
            int newZustand = zustand[4];


            Debug.WriteLine("Zustand: " + newZustand);
            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.CurrentZustand = newZustand;
                }
            });
        }

        private static void setBStunden(byte[] zustand)
        {
            
            int newBStunden = BitConverter.ToInt16(new byte[] { zustand[4], zustand[5] }, 0);
            Debug.WriteLine("Betriebsstunden: " + newBStunden);
            
           
            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.CurrentBStunden = newBStunden;
                    Betriebsstunden = newBStunden;
                }
            });
        }

        private static void setFahrtZahler(byte[] zustand)
        {
           
            int newFahrtzahler = BitConverter.ToInt16(new byte[] { zustand[4], zustand[5] }, 0);
            Debug.WriteLine("Fahrtzähler: " + newFahrtzahler);
            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.CurrentFahrtZahler = newFahrtzahler;
                }
            });

        }

        private static void setDoorState1(byte[] zustand)
        {
           
            int newDoorState = BitConverter.ToInt16(new byte[] { zustand[5], zustand[4] }, 0);
            Debug.WriteLine("DoorState: " + newDoorState);
            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.CurrentStateTueur1 = newDoorState;
                }
            });
        }

        private static void setDoorState2(byte[] zustand)
        {
            Debug.WriteLine(zustand);
            int newDoorState = BitConverter.ToInt16(new byte[] { zustand[5], zustand[4] }, 0);
            Debug.WriteLine("DoorState: " + newDoorState);
            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.CurrentStateTueur2 = newDoorState;
                }
            });
        }

        private static void setDoorState3(byte[] zustand)
        {
            Debug.WriteLine(zustand);
            int newDoorState = BitConverter.ToInt16(new byte[] { zustand[5], zustand[4] }, 0);
            Debug.WriteLine("DoorState: " + newDoorState);
            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.CurrentStateTueur3 = newDoorState;
                }
            });
        }

        private static void setFahrkorb(byte[] zustand)
        {
            int newFahrkorb = BitConverter.ToInt32(new byte[] { zustand[4], zustand[5], zustand[6], zustand[7] }, 0);
            Debug.WriteLine("Fahrkorb: " + newFahrkorb);
            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.CurrentFahrkorb = newFahrkorb;
                }
            });
        }

        // Eine statische Variable, um den vorherigen Y-Wert zu speichern (für Smoothing)
        public static void setFahrkorbAnimationPosition(byte[] zustand)
        {
            // Lese den Fahrkorbwert aus dem Byte-Array (als Int32)
            int newFahrkorb = BitConverter.ToInt32(zustand, 4);

            // Gesamtzahl der Etagen und das Etagen-Inkremente-Array
            int floorCount = GesamtFloor; // GesamtFloor sollte hier als int verfügbar sein
            int[] originalEtagen = LievViewManager.IngrementEtage;

            // Wert der obersten Etage (wird für die Normalisierung verwendet)
            int topFloorValue = originalEtagen[floorCount - 1];

            // Normalisiere die Etagenwerte und den Fahrkorbwert:
            // Das Ergebnis: oberste Etage = 0, darunter negative Werte.
            float[] normalizedEtagen = new float[floorCount];
            for (int i = 0; i < floorCount; i++)
            {
                normalizedEtagen[i] = originalEtagen[i] - topFloorValue;
            }
            float normalizedFahrkorb = newFahrkorb - topFloorValue;

            // UI-Schritt in Y-Einheiten (z. B. 95 pro Etage)
            float yStep = 95f;
            float YPosition = 0f;

            // Clamping, falls der Fahrkorbwert außerhalb des bekannten Bereichs liegt:
            if (normalizedFahrkorb <= normalizedEtagen[0])
            {
                // Unterste Etage
                YPosition = -((floorCount - 1) * yStep);
            }
            else if (normalizedFahrkorb >= normalizedEtagen[floorCount - 1])
            {
                // Oberste Etage
                YPosition = 0f;
            }
            else
            {
                // Finde das Intervall, in dem normalizedFahrkorb liegt.
                for (int i = 0; i < floorCount - 1; i++)
                {
                    if (normalizedFahrkorb >= normalizedEtagen[i] && normalizedFahrkorb <= normalizedEtagen[i + 1])
                    {
                        // Berechne den Anteil innerhalb des Intervalls:
                        // fraction = 0  -> genau an Etage i+1 (höher)
                        // fraction = 1  -> genau an Etage i (niedriger)
                        float fraction = (normalizedFahrkorb - normalizedEtagen[i]) /
                                         (normalizedEtagen[i + 1] - normalizedEtagen[i]);

                        // Berechne den UI-Wert der unteren Etage (i) in diesem Intervall:
                        // UI(i) = -((floorCount - 1 - i) * yStep)
                        float uiLower = -((floorCount - 1 - i) * yStep);

                        // Da die Differenz zwischen zwei Etagen immer yStep (z. B. 95) beträgt,
                        // erhalten wir die interpolierte Y-Position:
                        YPosition = uiLower + fraction * yStep;
                        break;
                    }
                }
            }

            Debug.WriteLine("FahrkorbAnimationY: " + YPosition);

            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.PositionY = YPosition;
                    LastKorbPosition = YPosition;
                }
            });
        }

        public static void innenruftasterquittung(byte[] zustand)
        {
            int Etage = zustand[2];
            int zustandQuit = zustand[4];

            if(zustandQuit == 0)
            {
                Debug.WriteLine("Innenruftasterquittung Etage: " + Etage + " beendet");
                int zustandQuittung = 1;
            }
            else
            {
                Debug.WriteLine("Innenruftasterquittung Etage: " + Etage + " gestartet");
                int zustandQuittung = 0;
            }


            // Aktualisiere das ViewModel im UI-Thread:
              Dispatcher.UIThread.Post(() =>
              {
                  if (MainWindow.Instance?.ViewModel != null)
                  {
                      MainWindow.Instance.ViewModel.InnenruftasterquittungEtage = Etage;
                      MainWindow.Instance.ViewModel.InnenruftasterquittungZustand = zustandQuit;
                  }
              });

        }

        public static void aufAussentasterquittung(byte[] zustand)
        {
            int Etage = zustand[2];
            int zustandQuit = zustand[4];

            if (zustandQuit == 0)
            {
                Debug.WriteLine("Innenruftasterquittung Etage: " + Etage + " beendet");
                int zustandQuittung = 1;
            }
            else
            {
                Debug.WriteLine("Innenruftasterquittung Etage: " + Etage + " gestartet");
                int zustandQuittung = 0;
            }


            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.AufAruftasterquittungEtage = Etage;
                    MainWindow.Instance.ViewModel.AufAruftasterquittungZustand = zustandQuit;
                }
            });

        }

        public static void abAussentasterquittung(byte[] zustand)
        {
            int Etage = zustand[2];
            int zustandQuit = zustand[4];

            if (zustandQuit == 0)
            {
                Debug.WriteLine("Innenruftasterquittung Etage: " + Etage + " beendet");
                int zustandQuittung = 1;
            }
            else
            {
                Debug.WriteLine("Innenruftasterquittung Etage: " + Etage + " gestartet");
                int zustandQuittung = 0;
            }


            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.AbAruftasterquittungEtage = Etage;
                    MainWindow.Instance.ViewModel.AbAruftasterquittungZustand = zustandQuit;
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
        public static void AnalyzeResponseNew(byte[] response)
        {

            if (response[0] == 0x21 && response[1] == 0x01)
            {
                Debug.WriteLine("Etagenänderung erkannt.");
                setCurrentFloor(response);
            }

            else if (response[0] == 0x26 && response[1] == 0x48)
            {
                Debug.WriteLine("Temperaturänderung erkannt.");
                setTemp(response);
            }

            else if (response[0] == 0x21 && response[1] == 0x02)
            {
                Debug.WriteLine("SK-Änderung erkannt.");
                setSK(response);
            }

            else if (response[0] == 0x64 && response[1] == 0x80)
            {
                Debug.WriteLine("Last-Änderung erkannt.");
                setLast(response);
            }

            else if (response[0] == 0x20 && response[1] == 0xFF)
            {
                Debug.WriteLine("Zustand-Änderung erkannt.");
                setZustand(response);
            }

            else if (response[0] == 0x26 && response[1] == 0x4C)
            {
                Debug.WriteLine("Fahrtenzähler-Änderung erkannt.");
                setFahrtZahler(response);
            }

            else if (response[0] == 0x26 && response[1] == 0x4B)
            {
                Debug.WriteLine("Betriebsstunden-Änderung erkannt.");
                setBStunden(response);
            }

            else if (response[0] == 0x63 && response[1] == 0x01 && response[2] == 0x01)
            {

                Debug.WriteLine("Tür1-Änderung erkannt.");
                setDoorState1(response);
            }
            else if (response[0] == 0x63 && response[1] == 0x01 && response[2] == 0x02)
            {

                Debug.WriteLine("Tür2-Änderung erkannt.");
                setDoorState2(response);
            }
            else if (response[0] == 0x63 && response[1] == 0x01 && response[2] == 0x03)
            {

                Debug.WriteLine("Tür3-Änderung erkannt.");
                setDoorState3(response);
            }

            else if (response[0] == 0x63 && response[1] == 0x83)
            {

                Debug.WriteLine(DateTime.Now.ToString("HH:mm:ss:ffff") + "Fahrkorbposition-Änderung erkannt.");
                setFahrkorb(response);
                setFahrkorbAnimationPosition(response);
            }

            else if (response[0] == 0x21 && response[1] == 0x03)
            {

                Debug.WriteLine("Innenruftasterquittung erkannt.");
                innenruftasterquittung(response);


            }

            else if (response[0] == 0x21 && response[1] == 0x04)
            {

                Debug.WriteLine("Aufwärts-Außenrufasterquittung erkannt.");
                aufAussentasterquittung(response);


            }

            else if (response[0] == 0x21 && response[1] == 0x05)
            {

                Debug.WriteLine("Abwärts-Außenrufasterquittung erkannt.");
                abAussentasterquittung(response);


            }

        }
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

                else if (response[6] == 0x26 && response[7] == 0x48)
                {
                    Debug.WriteLine("Temperaturänderung erkannt.");
                    setTemp(response);
                }

                else if (response[6] == 0x21 && response[7] == 0x02)
                {
                    Debug.WriteLine("SK-Änderung erkannt.");
                    setSK(response);
                }

                else if (response[6] == 0x64 && response[7] == 0x80)
                {
                    Debug.WriteLine("Last-Änderung erkannt.");
                    setLast(response);
                }

                else if (response[6] == 0x20 && response[7] == 0xFF)
                {
                    Debug.WriteLine("Zustand-Änderung erkannt.");
                    setZustand(response);
                }

                else if (response[6] == 0x26 && response[7] == 0x4C)
                {
                    Debug.WriteLine("Fahrtenzähler-Änderung erkannt.");
                    setFahrtZahler(response);
                }
                //Bisher nur hier Impelmentiert in ViewModel etc muss noch B-Stunden aktuallisiert werden.
                else if (response[6] == 0x26 && response[7] == 0x4B)
                {
                    Debug.WriteLine("Betriebsstunden-Änderung erkannt.");
                    setFahrtZahler(response);
                }

                else if (response[6] == 0x63 && response[7] == 0x83)
                {
                    Debug.WriteLine("Position-Änderung erkannt.");
                    setFahrtZahler(response);
                }
                // Weitere Zustände (z. B. A-Zustand) können hier analog verarbeitet werden.
            }
            else
            {
                Debug.WriteLine($"Telegrammart ist nicht 0x0502, sondern {response[4]:X2} {response[5]:X2}");
            }
        }
    }

    // Telegramm Breaker


public static class DataTypes
    {
        public const byte D_INTEGER16 = 0x03;
        public const byte D_UNSIGNED8 = 0x05;
        public const byte D_UNSIGNED16 = 0x06;
        public const byte D_UNSIGNED32 = 0x07;
        public const byte D_UNSIGNED48 = 0x19;
        public const byte D_REAL32 = 0x08;
        public const byte D_VIS_STRING = 0x09;  // Dynamisch, nullterminiert
        public const byte D_IDENTITY = 0x23;    // Entspricht nun D_UNSIGNED32, also 4 Byte
        public const byte D_DATE = 0x81;
    }

    public class TelegramProcessor
    {
        // Konstanten für Start- und Endekennung
        private const byte STX1 = 0x95;
        private const byte STX2 = 0x9A;
        private const byte ETX = 0x85;

        /// <summary>
        /// Ermittelt die Länge des Zustandswerts basierend auf dem Datentyp.
        /// Für D_VIS_STRING wird die Länge dynamisch ermittelt (bis zum Nullterminator).
        /// </summary>
        /// <param name="dataType">Der Datentyp.</param>
        /// <param name="telegram">Das Telegramm-Array.</param>
        /// <param name="pos">Aktuelle Position im Telegramm, ab der der Zustandswert steht.</param>
        /// <param name="payloadEnd">Endposition des Nutzdatenbereichs.</param>
        /// <returns>Die Länge in Byte, oder 0 bei unbekanntem Datentyp.</returns>
        private int GetValueLength(byte dataType, byte[] telegram, int pos, int payloadEnd)
        {
            switch (dataType)
            {
                case DataTypes.D_INTEGER16: return 2;
                case DataTypes.D_UNSIGNED8: return 1;
                case DataTypes.D_UNSIGNED16: return 2;
                case DataTypes.D_UNSIGNED32: return 4;
                case DataTypes.D_UNSIGNED48: return 6;
                case DataTypes.D_REAL32: return 4;
                case DataTypes.D_VIS_STRING:
                    // Länge dynamisch bestimmen: Durchlauf bis zum Nullterminator (0x00)
                    int start = pos;
                    while (pos < payloadEnd && telegram[pos] != 0)
                    {
                        pos++;
                    }
                    // Inklusive des Nullterminators (falls gefunden)
                    return (pos < payloadEnd && telegram[pos] == 0) ? (pos - start + 1) : (pos - start);
                case DataTypes.D_IDENTITY: return 4; // entspricht D_UNSIGNED32
                case DataTypes.D_DATE: return 8;
                default:
                    return 0; // Unbekannter Datentyp
            }
        }

        /// <summary>
        /// Verarbeitet ein komplettes Telegramm.
        /// Das Telegramm wird in einzelne Daten-Items aufgeteilt.
        /// </summary>
        /// <param name="telegram">Das Telegramm als Byte-Array.</param>
        public void ProcessTelegram(byte[] telegram)
        {
            // Mindestlänge prüfen: mindestens STX (2), Länge (2), Telegrammart (2), CRC (1) und ETX (1)
            if (telegram.Length < 8)
            {
                DebugPrintError("Telegramm zu kurz");
                return;
            }

            // Überprüfen der Startbytes
            if (telegram[0] != STX1 || telegram[1] != STX2)
            {
                DebugPrintError("Ungültige Startbytes");
                return;
            }

            // Datenlänge extrahieren (angenommen Big-Endian)
            ushort dataLength = (ushort)((telegram[2] << 8) | telegram[3]);
            if (dataLength != telegram.Length)
            {
                DebugPrintError("Inkonsistente Datenlänge");
                return;
            }

            // Überprüfen der Telegrammart (Bytes 4 und 5)
            if (telegram[4] != 0x05 || telegram[5] != 0x02)
            {
                Debug.WriteLine("Telegrammart passt nicht, wird übersprungen.");
                return;
            }

            // Nutzdatenbereich: ab Index 6 bis zu (dataLength - 2) (CRC und ETX am Ende)
            int payloadStart = 6;
            int payloadEnd = dataLength - 2; // Letzter Index des CRC ist dataLength-2, ETX ist dataLength-1
            int pos = payloadStart;

            // Solange noch Nutzdaten vorhanden sind
            while (pos < payloadEnd)
            {
                // Prüfen, ob mindestens 4 Byte (Index (2), Subindex (1), Datentyp (1)) vorhanden sind
                if (pos + 4 > payloadEnd)
                {
                    DebugPrintError("Nicht genügend Bytes für einen neuen Dateneintrag");
                    break;
                }

                // Zustandsindex (2 Byte)
                byte[] stateIndex = new byte[2] { telegram[pos], telegram[pos + 1] };
                pos += 2;

                // Zustands-Subindex (1 Byte)
                byte subIndex = telegram[pos++];

                // Datentyp (1 Byte)
                byte dataType = telegram[pos++];

                // Ermitteln der Länge des Zustandswerts (dynamisch für D_VIS_STRING)
                int valueLength = GetValueLength(dataType, telegram, pos, payloadEnd);
                if (valueLength == 0)
                {
                    DebugPrintError("Unbekannter Datentyp");
                    break;
                }

                // Prüfen, ob genügend Bytes für den Zustandswert vorhanden sind
                if (pos + valueLength > payloadEnd)
                {
                    DebugPrintError("Nicht genügend Bytes für Zustandswert");
                    break;
                }

                // Zustandswert extrahieren
                byte[] value = new byte[valueLength];
                Array.Copy(telegram, pos, value, 0, valueLength);
                pos += valueLength;

                // Neues Telegramm zusammenbauen: 2 Byte Index, 1 Byte Subindex, 1 Byte Datentyp, Zustandswert
                int newTelegramLen = 2 + 1 + 1 + valueLength;
                byte[] newTelegram = new byte[newTelegramLen];
                int offset = 0;
                Array.Copy(stateIndex, 0, newTelegram, offset, 2);
                offset += 2;
                newTelegram[offset++] = subIndex;
                newTelegram[offset++] = dataType;
                Array.Copy(value, 0, newTelegram, offset, valueLength);

                // Weiterleiten des neuen Telegramms an die weitere Verarbeitung
                MonetoringManager.AnalyzeResponseNew(newTelegram);
                ProcessNewTelegram(newTelegram);
            }

            // Optional: Weitere Validierung von CRC und ETX könnte hier erfolgen.
        }

        /// <summary>
        /// Beispielhafte Weiterverarbeitung eines neuen Telegramms.
        /// </summary>
        /// <param name="data">Das neue Telegramm als Byte-Array.</param>
        private void ProcessNewTelegram(byte[] data)
        {
            // Hier wird das neue Telegramm z. B. weitergereicht.
            Debug.Write("Neues Telegramm (Länge " + data.Length + "): ");
            foreach (byte b in data)
            {
                Debug.Write(b.ToString("X2") + " ");
            }
            Debug.WriteLine("");
        }

        /// <summary>
        /// Gibt Debug-Fehlermeldungen aus.
        /// </summary>
        /// <param name="msg">Fehlermeldungstext.</param>
        private void DebugPrintError(string msg)
        {
            Debug.WriteLine("Fehler: " + msg);
        }
    }




}

