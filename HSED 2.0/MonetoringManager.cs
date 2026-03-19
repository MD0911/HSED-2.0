using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using System.Collections.Generic;
using Avalonia.Threading;
using HSED_2_0.ViewModels;
using HSED_2._0;
using System.Security.Cryptography.X509Certificates;

namespace HSED_2_0
{
    public class MonetoringManager
    {
        // Statische Parameter für Etagenwerte:
        public static int BootFloor { get; private set; }
        public static int TopFloor { get; private set; }
        public static int GesamtFloor { get; private set; }
        public static int RawGesamtFloor { get; private set; }
        public static int CurrentFloor { get; private set; } // umgerechneter Floor: raw + BootFloor - 1
        public static int CurrentTemp { get; private set; }
        public static int CurrentSK1 { get; private set; }
        public static int CurrentSK2 { get; private set; }

        public static int Betriebsstunden { get; private set; }

        public static float LastKorbPosition { get; private set; }
        public static int LastRawKorbPosition { get; private set; }
        public static int CurrentSK3 { get; private set; }

        public static int CurrentSK4 { get; private set; }
        public static int CurrentLast { get; private set; }
        public static int CurrentZustand { get; private set; }
        public static bool IsSetupReady { get; private set; } = true;
        public static bool IsSetupReadyKnown { get; private set; }
        private static readonly Dictionary<int, string> _floorSigns = new();
        private static readonly object _setupReadyLock = new();


        // ===== Throttle nur für Fahrkorbposition (0x63 0x83) =====
        private static readonly long _posMinIntervalTicks = Stopwatch.Frequency / 30; // 30 FPS
        private static long _posNextAllowedTick = 0;

        // Immer das letzte Telegramm merken
        private static byte[] _pendingPosTelegram = null;

        // Ein einziger Timer, kein Task.Run
        private static System.Threading.Timer _posTimer = null;
        private static int _posTimerArmed = 0; // 0 = nicht geplant, 1 = geplant
        private static readonly object _posTimerLock = new object();





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
        public void Stop()
        {
            _cts?.Cancel();

            lock (_posTimerLock)
            {
                _posTimer?.Dispose();
                _posTimer = null;
            }

            Volatile.Write(ref _pendingPosTelegram, null);
            Volatile.Write(ref _posNextAllowedTick, 0);
            Volatile.Write(ref _posTimerArmed, 0);
        }


        /// <summary>
        /// Initialisiert einmalig BootFloor und TopFloor und berechnet GesamtFloor.
        /// Diese Methode MUSS beim Start des Monitoring-Prozesses aufgerufen werden.
        /// </summary>
        public static void startMonetoring()
        {
            RefreshSetupReadyStateFromParameterRead();

            byte[] bootFloorResponse = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x00, 0x00, 0x03 });
            byte[] topfloorResponse = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x01, 0x00, 0x03 });

            if (bootFloorResponse == null || bootFloorResponse.Length <= 10 ||
                topfloorResponse == null || topfloorResponse.Length <= 10)
            {
                Debug.WriteLine("Fehler beim Abfragen von BootFloor/TopFloor.");
                return;
            }

            BootFloor = bootFloorResponse[10];
            TopFloor = topfloorResponse[10];

            RawGesamtFloor = HseCom.SendHse(1001);
            GesamtFloor = RawGesamtFloor > 0 ? RawGesamtFloor : (TopFloor - BootFloor) + 1;
            LoadFloorSigns();
            Debug.WriteLine("BootFloor: " + BootFloor);
            Debug.WriteLine("TopFloor: " + TopFloor);
            Debug.WriteLine("RawGesamtFloor: " + RawGesamtFloor);
            Debug.WriteLine("GesamtFloor: " + GesamtFloor);
        }

        public static void RefreshSetupReadyStateFromParameterRead()
        {
            int setupReadyValue = ReadSetupReadyValue();
            if (setupReadyValue < 0)
                return;

            UpdateSetupReadyState(setupReadyValue != 0, requestRefreshOnChange: false, "single-read");
        }

        private static int ReadSetupReadyValue()
        {
            byte[] response = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x0A, 0x00, 0x05 });
            if (response == null || response.Length <= 10)
            {
                Debug.WriteLine("Setup Beendet konnte nicht gelesen werden.");
                return -1;
            }

            byte value = response[10];
            Debug.WriteLine($"Setup Beendet (0x240A/0x00) Einzelabfrage: {value}");
            return value;
        }

        private static void HandleSetupReadyMonitoring(byte[] response)
        {
            if (response == null || response.Length < 5)
                return;

            if (response[3] != DataTypes.D_UNSIGNED8)
            {
                Debug.WriteLine($"Setup Beendet mit unerwartetem Datentyp empfangen: 0x{response[3]:X2}");
                return;
            }

            byte value = response[4];
            Debug.WriteLine($"Setup Beendet (0x240A/0x00) Monitoring: {value}");
            UpdateSetupReadyState(value != 0, requestRefreshOnChange: true, "monitoring");
        }

        private static void UpdateSetupReadyState(bool isSetupReady, bool requestRefreshOnChange, string source)
        {
            bool shouldRefresh = false;
            bool previousValue = true;

            lock (_setupReadyLock)
            {
                bool hadKnownValue = IsSetupReadyKnown;
                previousValue = IsSetupReady;

                IsSetupReady = isSetupReady;
                IsSetupReadyKnown = true;

                shouldRefresh = requestRefreshOnChange && hadKnownValue && previousValue != isSetupReady;
            }

            Debug.WriteLine($"Setup Beendet aktualisiert via {source}: {(isSetupReady ? 1 : 0)}");

            if (shouldRefresh)
            {
                Debug.WriteLine($"Setup Beendet wechselte von {(previousValue ? 1 : 0)} auf {(isSetupReady ? 1 : 0)}. Soft-Refresh wird gestartet.");
                RequestSoftRefreshWithoutConfirmation();
            }
        }

        private static void RequestSoftRefreshWithoutConfirmation()
        {
            Dispatcher.UIThread.Post(() =>
            {
                var mainWindow = MainWindow.Instance;
                if (mainWindow != null)
                    _ = TouchDisplayRefreshService.RequestRefreshWithoutConfirmationAsync(mainWindow);
            });
        }

        public static int GetFirstAbsoluteFloorIndex1Based()
        {
            return BootFloor + 1;
        }

        public static int GetLastAbsoluteFloorIndex1Based()
        {
            return TopFloor + 1;
        }

        public static int GetLocalFloorArrayIndexFromRawFloor(int rawFloor)
        {
            return rawFloor - BootFloor;
        }

        public static void LoadLevelIncrementsInto(int[] target)
        {
            if (target == null || target.Length == 0)
                return;

            Array.Clear(target, 0, target.Length);

            int firstAbsoluteFloorIndex = GetFirstAbsoluteFloorIndex1Based();
            int floorCount = RawGesamtFloor > 0 ? RawGesamtFloor : GesamtFloor;

            for (int i = 0; i < floorCount && i < target.Length; i++)
            {
                int floorIndex1Based = firstAbsoluteFloorIndex + i;
                byte[] response = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x29, (byte)floorIndex1Based });

                if (response == null || response.Length <= 13)
                {
                    Debug.WriteLine($"Ungültige Antwort für Increment Etage {floorIndex1Based}");
                    continue;
                }

                target[i] = BitConverter.ToInt32(new byte[] { response[10], response[11], response[12], response[13] }, 0);
                Debug.WriteLine($"Increment Etage {floorIndex1Based}: {target[i]}");
            }
        }

        private static void LoadFloorSigns()
        {
            _floorSigns.Clear();

            int floorCount = RawGesamtFloor > 0 ? RawGesamtFloor : GesamtFloor;
            int firstAbsoluteFloorIndex = GetFirstAbsoluteFloorIndex1Based();

            for (int i = 0; i < floorCount; i++)
            {
                int floorIndex1Based = firstAbsoluteFloorIndex + i;
                byte[] response = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, (byte)floorIndex1Based, 0x03 });
                _floorSigns[floorIndex1Based] = DecodeFloorSign(response);
            }
        }

        private static string DecodeFloorSign(byte[] response)
        {
            if (response == null || response.Length <= 11)
                return string.Empty;

            byte[] floorName = new byte[2];
            floorName[0] = response[11];
            floorName[1] = response[10];
            string sign = Encoding.ASCII.GetString(floorName);
            return sign.Trim('\0', ' ', '\t', '\r', '\n');
        }

        public static string GetFloorDisplayText(int floorIndex1Based)
        {
            if (floorIndex1Based <= 0)
                return string.Empty;

            if (_floorSigns.TryGetValue(floorIndex1Based, out string sign) && !string.IsNullOrWhiteSpace(sign))
                return sign;

            return (BootFloor + floorIndex1Based - 1).ToString();
        }

        public static string GetFloorDisplayTextFromRawFloor(int rawFloor)
        {
            return GetFloorDisplayText(rawFloor + 1);
        }

        /// <summary>
        /// Aktualisiert CurrentFloor anhand eines Monitoring-Telegramms (angenommen ab Offset 10).
        /// Umrechnung: CurrentFloor = rawFloor + BootFloor - 1.
        /// </summary>
        /// 

        private static void HandleFahrkorbThrottled(byte[] telegram)
        {
            if (telegram == null)
                return;

            // Immer das letzte Telegramm behalten
            var copy = new byte[telegram.Length];
            Buffer.BlockCopy(telegram, 0, copy, 0, telegram.Length);
            Volatile.Write(ref _pendingPosTelegram, copy);


            long now = Stopwatch.GetTimestamp();
            long nextAllowed = Volatile.Read(ref _posNextAllowedTick);

            // Wenn wir sofort dürfen, sofort anwenden
            if (now >= nextAllowed)
            {
                Volatile.Write(ref _posNextAllowedTick, now + _posMinIntervalTicks);

                var t = Interlocked.Exchange(ref _pendingPosTelegram, null);
                if (t != null)
                {
                    setFahrkorb(t);
                    setFahrkorbAnimationPosition(t);
                }

                return;
            }

            // Sonst Timer genau einmal scharf schalten
            if (Interlocked.Exchange(ref _posTimerArmed, 1) == 1)
                return;

            long delayTicks = nextAllowed - now;
            int delayMs = (int)Math.Max(1, (delayTicks * 1000L) / Stopwatch.Frequency);

            lock (_posTimerLock)
            {
                if (_posTimer == null)
                {
                    _posTimer = new System.Threading.Timer(_ =>
                    {
                        try
                        {
                            if (Volatile.Read(ref _pendingPosTelegram) == null)
                                return;

                            long applyNow = Stopwatch.GetTimestamp();
                            Volatile.Write(ref _posNextAllowedTick, applyNow + _posMinIntervalTicks);

                            var t2 = Interlocked.Exchange(ref _pendingPosTelegram, null);
                            if (t2 != null)
                            {
                                setFahrkorb(t2);
                                setFahrkorbAnimationPosition(t2);
                            }
                        }
                        finally
                        {
                            Volatile.Write(ref _posTimerArmed, 0);

                            // Falls direkt wieder neue Daten reinkamen, erneut planen
                            if (Volatile.Read(ref _pendingPosTelegram) != null)
                            {
                                HandleFahrkorbThrottled(Volatile.Read(ref _pendingPosTelegram));
                            }
                        }
                    }, null, delayMs, Timeout.Infinite);
                }
                else
                {
                    _posTimer.Change(delayMs, Timeout.Infinite);
                }
            }
        }


        private static void setCurrentFloor(byte[] currentFloorResponse)
        {
          

            int rawFloor = currentFloorResponse[4];
            Debug.WriteLine("Rohwert: " + rawFloor);
            Debug.WriteLine("BootFloor: " + BootFloor);
            CurrentFloor = rawFloor + 1;
            Debug.WriteLine($"setCurrentFloor: raw = {rawFloor}, BootFloor = {BootFloor}, CurrentFloor = {CurrentFloor}");

            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.RawCurrentFloor = rawFloor;
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
            LiftStateTracker.RegisterState(newZustand);
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
            if (!TryReadMonitoringNumericValue(zustand, out int newBStunden))
            {
                return;
            }
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

        public static void ApplySingleReadCurrentFloor(int rawFloor)
        {
            if (rawFloor == 505 || rawFloor < 0)
            {
                return;
            }

            CurrentFloor = rawFloor + 1;
            Debug.WriteLine($"Etage Einzelabfrage: raw = {rawFloor}, CurrentFloor = {CurrentFloor}");

            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.RawCurrentFloor = rawFloor;
                    MainWindow.Instance.ViewModel.CurrentFloor = CurrentFloor;
                }
            });
        }

        public static void ApplySingleReadBetriebsstunden(int newBStunden)
        {
            if (newBStunden == 505 || newBStunden < 0)
            {
                return;
            }

            Debug.WriteLine("Betriebsstunden Einzelabfrage: " + newBStunden);

            Dispatcher.UIThread.Post(() =>
            {
                Betriebsstunden = newBStunden;

                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.CurrentBStunden = newBStunden;
                }
            });
        }

        private static void setFahrtZahler(byte[] zustand)
        {
            if (!TryReadMonitoringNumericValue(zustand, out int newFahrtzahler))
            {
                return;
            }
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

        private static bool TryReadMonitoringNumericValue(byte[] zustand, out int value)
        {
            value = 0;

            if (zustand == null || zustand.Length < 5)
            {
                return false;
            }

            byte dataType = zustand[3];

            switch (dataType)
            {
                case DataTypes.D_UNSIGNED8:
                    value = zustand[4];
                    return true;

                case DataTypes.D_INTEGER16:
                    if (zustand.Length < 6)
                        return false;
                    value = BitConverter.ToInt16(new byte[] { zustand[4], zustand[5] }, 0);
                    return true;

                case DataTypes.D_UNSIGNED16:
                    if (zustand.Length < 6)
                        return false;
                    value = BitConverter.ToUInt16(new byte[] { zustand[4], zustand[5] }, 0);
                    return true;

                case DataTypes.D_UNSIGNED32:
                case DataTypes.D_IDENTITY:
                    if (zustand.Length < 8)
                        return false;
                    uint value32 = BitConverter.ToUInt32(new byte[] { zustand[4], zustand[5], zustand[6], zustand[7] }, 0);
                    if (value32 > int.MaxValue)
                    {
                        Debug.WriteLine($"Monitoringwert überschreitet Int32: {value32}");
                        value = int.MaxValue;
                        return true;
                    }
                    value = (int)value32;
                    return true;

                default:
                    Debug.WriteLine($"Unerwarteter Datentyp für numerischen Monitoringwert: 0x{dataType:X2}");
                    return false;
            }
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
            LastRawKorbPosition = newFahrkorb;
            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.CurrentFahrkorb = newFahrkorb;
                }
            });
        }

        public static void ApplySingleReadFahrkorb(int rawPosition)
        {
            if (rawPosition == 505 || rawPosition < 0)
            {
                return;
            }

            Debug.WriteLine("Fahrkorb Einzelabfrage: " + rawPosition);
            LastRawKorbPosition = rawPosition;

            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.CurrentFahrkorb = rawPosition;
                }
            });
        }

        public static void setFahrkorbAnimationPosition(byte[] zustand)
        {
            if (!TryCalculateFahrkorbAnimationPosition(zustand, out float y))
                return;

            Debug.WriteLine("FahrkorbAnimationY: " + y);
            LastKorbPosition = y;

            Dispatcher.UIThread.Post(() =>
            {
                var vm = MainWindow.Instance?.ViewModel;
                if (vm != null)
                {
                    vm.PositionY = y;
                }
            });
        }

        private static bool TryCalculateFahrkorbAnimationPosition(byte[] zustand, out float y)
        {
            y = 0f;

            const float yStep = 95f;
            const float yOffsetPx = 0f;

            if (zustand == null || zustand.Length < 8)
                return false;

            int raw = BitConverter.ToInt32(zustand, 4);
            float value = raw;

            int[] source = LievViewManager.IngrementEtage;
            if (source == null || source.Length == 0 || GesamtFloor <= 0)
                return false;

            int floorCount = Math.Min(GesamtFloor, source.Length);
            if (floorCount <= 0)
                return false;

            int[] floors = new int[floorCount];
            Array.Copy(source, floors, floorCount);
            Array.Sort(floors);

            if (floorCount == 1 || float.IsNaN(value) || float.IsInfinity(value))
            {
                y = 0f + yOffsetPx;
                return true;
            }

            static int FindFirstNonZeroSpan(int[] arr)
            {
                for (int i = 0; i < arr.Length - 1; i++)
                    if (arr[i + 1] > arr[i]) return i;
                return -1;
            }

            static int FindLastNonZeroSpan(int[] arr)
            {
                for (int i = arr.Length - 2; i >= 0; i--)
                    if (arr[i + 1] > arr[i]) return i;
                return -1;
            }

            if (value < floors[0])
            {
                int idx = FindFirstNonZeroSpan(floors);
                if (idx < 0)
                {
                    y = -((floorCount - 1) * yStep);
                }
                else
                {
                    int a = floors[idx];
                    int b = floors[idx + 1];
                    float span = b - a;
                    float tExtra = (value - floors[0]) / span;
                    y = -((floorCount - 1) * yStep) + tExtra * yStep;
                }

                y += yOffsetPx;
                return true;
            }

            if (value > floors[floorCount - 1])
            {
                int idx = FindLastNonZeroSpan(floors);
                if (idx < 0)
                {
                    y = 0f;
                }
                else
                {
                    int a = floors[idx];
                    int b = floors[idx + 1];
                    float span = b - a;
                    float tExtra = (value - floors[floorCount - 1]) / span;
                    y = 0f + tExtra * yStep;
                }

                y += yOffsetPx;
                return true;
            }

            int lo = 0, hi = floorCount;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (floors[mid] <= value) lo = mid + 1; else hi = mid;
            }

            int i = lo - 1;
            if (i < 0) i = 0;
            if (i > floorCount - 2) i = floorCount - 2;

            int leftVal = floors[i];
            int rightVal = floors[i + 1];
            float spanLR = rightVal - leftVal;

            if (spanLR == 0f)
            {
                int left = i;
                while (left > 0 && floors[left - 1] == leftVal) left--;
                y = -((floorCount - 1 - left) * yStep);
            }
            else
            {
                float t = (value - leftVal) / spanLR;
                if (t < 0f) t = 0f; else if (t > 1f) t = 1f;

                float yLower = -((floorCount - 1 - i) * yStep);
                y = yLower + t * yStep;
            }

            y += yOffsetPx;
            return true;
        }

        public static void ApplySingleReadKorbPosition(int rawPosition)
        {
            if (rawPosition == 505 || rawPosition < 0)
            {
                return;
            }

            ApplySingleReadFahrkorb(rawPosition);

            byte[] pseudoMonitoringTelegram = new byte[8];
            byte[] rawBytes = BitConverter.GetBytes(rawPosition);
            Array.Copy(rawBytes, 0, pseudoMonitoringTelegram, 4, rawBytes.Length);

            Debug.WriteLine("Korbposition Einzelabfrage roh: " + rawPosition);
            setFahrkorbAnimationPosition(pseudoMonitoringTelegram);
        }

        public static void ApplySingleReadDoorZone(int rawDoorZone)
        {
            if (rawDoorZone == 505 || rawDoorZone < 0 || rawDoorZone > byte.MaxValue)
            {
                return;
            }

            Debug.WriteLine($"Signalgeber Einzelabfrage: 0x{rawDoorZone:X2}");
            UpdateDoorZone((byte)rawDoorZone);
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

        public static void speed(byte[] zustand)
        {

            int speed = BitConverter.ToInt16(new byte[] { zustand[4], zustand[5] }, 0);

            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.Speed = speed;
                }
            });
        }

        public static void signal(byte[] zustand)
        {
            byte signal = zustand[4];
            UpdateDoorZone(signal);
        }

        private static void UpdateDoorZone(byte signal)
        {
            bool sgm = (signal & 0x01) != 0;

            // The original DFUE logic evaluates bits 1-2 as one 2-bit field.
            int upperLowerState = (signal >> 1) & 0x03;
            bool sgo = upperLowerState == 1 || upperLowerState == 3;
            bool sgu = upperLowerState == 2 || upperLowerState == 3;

            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.DoorZone = signal;
                    MainWindow.Instance.ViewModel.SGM = sgm;
                    MainWindow.Instance.ViewModel.SGO = sgo;
                    MainWindow.Instance.ViewModel.SGU = sgu;
                }
            });
        }

        public static void setSKF(byte[] zustand)
        {
            if (MainWindow.Instance.ViewModel.SKFActive)
            {
                int skf = zustand[4];
                Debug.WriteLine("SKF: " + skf);
                Debug.WriteLine(zustand);

                // Aktualisiere das ViewModel im UI-Thread:
                Dispatcher.UIThread.Post(() =>
                {
                    if (MainWindow.Instance?.ViewModel != null)
                    {
                        MainWindow.Instance.ViewModel.SKF = skf;
                    }
                });
            }
        }

        public static void setSKFActive(byte[] zustand)
        {
            int skf = zustand[4];
            Debug.WriteLine("SKFActive: " + skf);
            bool SkfActive = false;

            if (skf == 0)
            {
                SkfActive = false;
            }
            else if (skf == 1) 
            { 
                SkfActive = true;
            }



            // Aktualisiere das ViewModel im UI-Thread:
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.SKFActive = SkfActive;
                }
            });
        }

        public static void setLS(byte[] zustand, int tur)
        {
            

            // Aktualisiere das ViewModel im UI-Thread:

            switch(tur)
            {
                case 1:
                    int ls1 = zustand[4];
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (MainWindow.Instance?.ViewModel != null)
                        {
                            MainWindow.Instance.ViewModel.LS1 = ls1;
                        }
                    });
                    break;
                case 2:
                    int ls2 = zustand[4];
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (MainWindow.Instance?.ViewModel != null)
                        {
                            MainWindow.Instance.ViewModel.LS2 = ls2;
                        }
                    });
                    break;
                case 3:
                    int ls3 = zustand[4];
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (MainWindow.Instance?.ViewModel != null)
                        {
                            MainWindow.Instance.ViewModel.LS3 = ls3;
                        }
                    });
                    break;
                default:
                    break;
            }

        }

        public static void setDS(byte[] zustand, int tur)
        {
            if(tur == 1)
            {
                byte signal = zustand[4];
                bool DOP1;
                bool DCL1;
                bool DREV1;
                bool DOPNA1;
                if ((signal & 0x01) != 0)
                {
                    DOP1 = true;
                }
                else
                {
                    DOP1 = false;
                }
                if ((signal & 0x02) != 0)
                {
                    DCL1 = true;
                }
                else
                {
                    DCL1 = false;
                }
                if ((signal & 0x04) != 0)
                {
                    DREV1 = true;
                }
                else
                {
                    DREV1 = false;
                }
                if ((signal & 0x80) != 0)
                {
                   DOPNA1 = true;

                }
                else
                {
                   DOPNA1 = false;
                }
                Dispatcher.UIThread.Post(() =>
                {
                    if (MainWindow.Instance?.ViewModel != null)
                    {
                        MainWindow.Instance.ViewModel.DOP1 = DOP1;
                        MainWindow.Instance.ViewModel.DCL1 = DCL1;
                        MainWindow.Instance.ViewModel.DREV1 = DREV1;
                        MainWindow.Instance.ViewModel.DOPNA1 = DOPNA1;
                    }
                });
            }

            else if (tur == 2)
            {
                byte signal = zustand[4];
                bool DOP2;
                bool DCL2;
                bool DREV2;
                bool DOPNA2;
                if ((signal & 0x01) != 0)
                {
                    DOP2 = true;
                }
                else
                {
                    DOP2 = false;
                }
                if ((signal & 0x02) != 0)
                {
                    DCL2 = true;
                }
                else
                {
                    DCL2 = false;
                }
                if ((signal & 0x04) != 0)
                {
                    DREV2 = true;
                }
                else
                {
                    DREV2 = false;
                }
                if ((signal & 0x80) != 0)
                {
                    DOPNA2 = true;

                }
                else
                {
                    DOPNA2 = false;
                }
                Dispatcher.UIThread.Post(() =>
                {
                    if (MainWindow.Instance?.ViewModel != null)
                    {
                        MainWindow.Instance.ViewModel.DOP2 = DOP2;
                        MainWindow.Instance.ViewModel.DCL2 = DCL2;
                        MainWindow.Instance.ViewModel.DREV2 = DREV2;
                        MainWindow.Instance.ViewModel.DOPNA2 = DOPNA2;
                    }
                });
            }

            else if (tur == 3)
            {
                byte signal = zustand[4];
                bool DOP3;
                bool DCL3;
                bool DREV3;
                bool DOPNA3;
                if ((signal & 0x01) != 0)
                {
                    DOP3 = true;
                }
                else
                {
                    DOP3 = false;
                }
                if ((signal & 0x02) != 0)
                {
                    DCL3 = true;
                }
                else
                {
                    DCL3 = false;
                }
                if ((signal & 0x04) != 0)
                {
                    DREV3 = true;
                }
                else
                {
                    DREV3 = false;
                }
                if ((signal & 0x80) != 0)
                {
                   DOPNA3 = true;
                    
                }
                else
                {
                   DOPNA3 = false;
                }
                Dispatcher.UIThread.Post(() =>
                {
                    if (MainWindow.Instance?.ViewModel != null)
                    {
                        MainWindow.Instance.ViewModel.DOP3 = DOP3;
                        MainWindow.Instance.ViewModel.DCL3 = DCL3;
                        MainWindow.Instance.ViewModel.DREV3 = DREV3;
                        MainWindow.Instance.ViewModel.DOPNA3 = DOPNA3;
                    }
                });
            }




        }

        public static void setTime(byte[] zustand)
        {
            if (zustand == null || zustand.Length < 12)
                return; // oder Exception werfen

            // Zeit
            byte stunde = zustand[4];   // 0..23
            byte minute = zustand[5];   // 0..59
            byte sekunde = zustand[6];   // 0..59

            // Datum
            byte tag = zustand[8];            // z.B. 0x18 = 24
            byte rawMonat = zustand[9];            // z.B. 0x0C = 12
            int monat = rawMonat - 1;          // 12 - 1 = 11 → November
            int jahr = (zustand[10] << 8) | zustand[11]; // 0x07E9 = 2025

            string zeitString = $"{stunde:00}:{minute:00}:{sekunde:00}";
            string datumString = $"{tag:00}.{monat:00}.{jahr:0000}";

            string telegramm = BitConverter.ToString(zustand).Replace("-", " ");

            Debug.WriteLine("Telegramm: " + telegramm);
            Debug.WriteLine("Zeit: " + zeitString);
            Debug.WriteLine("Datum: " + datumString);
            Dispatcher.UIThread.Post(() =>
            {
                if (MainWindow.Instance?.ViewModel != null)
                {
                    MainWindow.Instance.ViewModel.CurrentTime = zeitString;
                    MainWindow.Instance.ViewModel.CurrentDate = datumString;

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
            if (response == null || response.Length < 2)
                return;

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
            else if (response[0] == 0x21 && response[1] == 0x61)
            {
                Debug.WriteLine("Fahrtenzähler-Änderung erkannt.");
                setFahrtZahler(response);
            }
            else if (response[0] == 0x21 && response[1] == 0x62)
            {
                Debug.WriteLine("Betriebsstunden-Änderung erkannt.");
                setBStunden(response);
            }
            else if (response[0] == 0x26 && response[1] == 0x4B)
            {
                Debug.WriteLine("0x264B empfangen, aber nicht als Betriebsstunden interpretiert.");
            }
            else if (response[0] == 0x26 && response[1] == 0x4C)
            {
                Debug.WriteLine("0x264C empfangen, aber nicht als Fahrtenzähler interpretiert.");
            }
            else if (response[0] == 0x63 && response[1] == 0x01 && response.Length >= 3 && response[2] == 0x01)
            {
                Debug.WriteLine("Tür1-Änderung erkannt.");
                setDoorState1(response);
            }
            else if (response[0] == 0x63 && response[1] == 0x01 && response.Length >= 3 && response[2] == 0x02)
            {
                Debug.WriteLine("Tür2-Änderung erkannt.");
                setDoorState2(response);
            }
            else if (response[0] == 0x63 && response[1] == 0x01 && response.Length >= 3 && response[2] == 0x03)
            {
                Debug.WriteLine("Tür3-Änderung erkannt.");
                setDoorState3(response);
            }
            else if (response[0] == 0x63 && response[1] == 0x83)
            {
                // NUR HIER throttlen
                HandleFahrkorbThrottled(response);
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
            else if (response[0] == 0x63 && response[1] == 0x90)
            {
                Debug.WriteLine("Geschwin. Änderung erkannt.");
                speed(response);
            }
            else if (response[0] == 0x26 && response[1] == 0x4F)
            {
                Debug.WriteLine("Signalgeber. Änderung erkannt.");
                signal(response);
            }
            else if (response[0] == 0x26 && response[1] == 0x50)
            {
                Debug.WriteLine("SKF. Änderung erkannt.");
                setSKF(response);
            }
            else if (response[0] == 0x26 && response[1] == 0x53)
            {
                Debug.WriteLine("SKF. Aktiv/Nicht Aktiv.");
                setSKFActive(response);
            }
            else if (response[0] == 0x63 && response[1] == 0x10 && response.Length >= 3 && response[2] == 0x01)
            {
                Debug.WriteLine("LS-T1. Änderung erkannt.");
                setLS(response, 1);
            }
            else if (response[0] == 0x63 && response[1] == 0x10 && response.Length >= 3 && response[2] == 0x02)
            {
                Debug.WriteLine("LS-T2. Änderung erkannt.");
                setLS(response, 2);
            }
            else if (response[0] == 0x63 && response[1] == 0x10 && response.Length >= 3 && response[2] == 0x03)
            {
                Debug.WriteLine("LS-T3. Änderung erkannt.");
                setLS(response, 3);
            }
            else if (response[0] == 0x63 && response[1] == 0xEF && response.Length >= 3 && response[2] == 0x01)
            {
                Debug.WriteLine("DS1. Änderung erkannt.");
                setDS(response, 1);
            }
            else if (response[0] == 0x63 && response[1] == 0xEF && response.Length >= 3 && response[2] == 0x02)
            {
                Debug.WriteLine("DS2. Änderung erkannt.");
                setDS(response, 2);
            }
            else if (response[0] == 0x63 && response[1] == 0xEF && response.Length >= 3 && response[2] == 0x03)
            {
                Debug.WriteLine("DS3. Änderung erkannt.");
                setDS(response, 3);
            }
            else if (response[0] == 0x26 && response[1] == 0x47)
            {
                Debug.WriteLine("Uhr Änderung erkannt.");
                setTime(response);
            }
            else if (response[0] == 0x24 && response[1] == 0xB5 && response.Length >= 5 && response[3] == DataTypes.D_UNSIGNED8)
            {
                Debug.WriteLine(
                    $"[Terminal][Monitoring] 0x24B5 empfangen: Leitung={response[2]}, " +
                    $"Spalten={response[4]}, aktive Leitung={TerminalManager.CurrentLine}, aktive Spalten={TerminalManager.CurrentColumns}");
                TerminalManager.ApplyTerminalCharacterWidthFromMonitoring(response[2], response[4]);
            }
            else if (response[0] == 0x24 && response[1] == 0x0A)
            {
                Debug.WriteLine("Setup Beendet Änderung erkannt.");
                HandleSetupReadyMonitoring(response);
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

                else if (response[6] == 0x21 && response[7] == 0x61)
                {
                    Debug.WriteLine("Fahrtenzähler-Änderung erkannt.");
                    setFahrtZahler(response);
                }
                //Bisher nur hier Impelmentiert in ViewModel etc muss noch B-Stunden aktuallisiert werden.
                else if (response[6] == 0x21 && response[7] == 0x62)
                {
                    Debug.WriteLine("Betriebsstunden-Änderung erkannt.");
                    setBStunden(response);
                }
                else if (response[6] == 0x26 && response[7] == 0x4B)
                {
                    Debug.WriteLine("0x264B empfangen, aber nicht als Betriebsstunden interpretiert.");
                }
                else if (response[6] == 0x26 && response[7] == 0x4C)
                {
                    Debug.WriteLine("0x264C empfangen, aber nicht als Fahrtenzähler interpretiert.");
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

