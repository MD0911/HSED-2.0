using HSED_2_0;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HSED_2._0
{
    public static class TestrufeService
    {
        private static CancellationTokenSource? _cts;

        public static void StartBackgroundUpdate()
        {
            if (_cts != null)
                return; // Bereits gestartet

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(async () =>
            {
                var vm = MainWindow.MainViewModelInstance;
                if (vm == null)
                {
                    Debug.WriteLine("MainViewModelInstance ist null. Hintergrund-Polling kann nicht gestartet werden.");
                    return;
                }

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // Beispiel: Daten aus HSE lesen und in ViewModel schreiben
                        vm.CurrentFloor = HseCom.SendHse(1002);
                        vm.CurrentZustand = HseCom.SendHse(1005);

                        // Temperatur
                        int temp = HseCom.SendHse(3001);
                        vm.CurrentTemp = temp;

                        // Last
                        byte[] last = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x64, 0x80 });
                        if (last != null && last.Length >= 10)
                        {
                            int LastValue = BitConverter.ToInt16(new byte[] { last[8], last[9] }, 0);
                            vm.CurrentLast = LastValue;
                        }

                        // SK‐Status (Beispiel)
                        byte[] skResponse = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x21, 0x02, 0x00, 0x05 });
                        if (skResponse != null && skResponse.Length > 10)
                        {
                            byte sk = skResponse[10];
                            vm.CurrentSK1 = (sk & (1 << 0)) != 0 ? 1 : 0;
                            vm.CurrentSK2 = (sk & (1 << 1)) != 0 ? 1 : 0;
                            vm.CurrentSK3 = (sk & (1 << 2)) != 0 ? 1 : 0;
                            vm.CurrentSK4 = (sk & (1 << 3)) != 0 ? 1 : 0;
                        }

                        // … weitere HSE‐Abfragen (Fahrtzähler, Türen, etc.) …

                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Fehler im Hintergrund-Polling: {ex}");
                    }

                    // Warte 5 Sekunden bis zur nächsten Aktualisierung
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), token);
                    }
                    catch (TaskCanceledException)
                    {
                        // Abbruch gewünscht
                        break;
                    }
                }
            }, token);
        }

        public static void StopBackgroundUpdate()
        {
            if (_cts == null)
                return;

            _cts.Cancel();
            _cts = null;
        }
    }
}
