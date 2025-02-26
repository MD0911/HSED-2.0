using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace HSED_2_0
{
    /// <summary>
    /// Verwaltet eine serielle Verbindung als Singleton – die Schnittstelle wird beim ersten Zugriff initialisiert.
    /// Eine öffentliche Open-Methode erlaubt es, die Verbindung manuell zu öffnen.
    /// </summary>
    public class SerialPortManager
    {
        private static readonly SerialPortManager _instance = new SerialPortManager();
        private SerialPort _serialPort;
        private readonly object _lock = new object();
        private CancellationTokenSource _cancellationTokenSource;
        private Task _listeningTask;
        // Alle kompletten Telegramme werden hier gesammelt
        private readonly ConcurrentQueue<byte[]> _telegramQueue = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _telegramAvailable = new SemaphoreSlim(0);

        private SerialPortManager()
        {
            _serialPort = new SerialPort("COM3", 38400, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 100,
                NewLine = "\r\n"
            };

            Open();

            _cancellationTokenSource = new CancellationTokenSource();
            _listeningTask = Task.Run(() => Listen(_cancellationTokenSource.Token));
        }

        public static SerialPortManager Instance => _instance;

        public void Open()
        {
            lock (_lock)
            {
                if (_serialPort != null && !_serialPort.IsOpen)
                {
                    try
                    {
                        _serialPort.Open();
                        Debug.WriteLine("Serielle Verbindung erfolgreich geöffnet (Open-Methode).");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Fehler beim Öffnen der seriellen Schnittstelle: {ex.Message}");
                    }
                }
                else
                {
                    Debug.WriteLine("Serielle Verbindung ist bereits geöffnet oder _serialPort ist null.");
                }
            }
        }

        /// <summary>
        /// Der Listener liest kontinuierlich vom Port, sammelt Bytes in einem lokalen Puffer,
        /// und sobald ein komplettes Telegramm (definiert durch das Footerbyte 0x85 und die Länge aus Index 3)
        /// empfangen wurde, wird es in die Queue gestellt und geloggt.
        /// </summary>
        private void Listen(CancellationToken token)
        {
            List<byte> buffer = new List<byte>();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    int byteRead = _serialPort.ReadByte(); // blockiert bis ein Byte oder Timeout
                    if (byteRead >= 0)
                    {
                        byte b = (byte)byteRead;
                        buffer.Add(b);

                        // Es werden mindestens 6 Bytes erwartet (Header, Längenfeld etc.)
                        if (b == 0x85 && buffer.Count >= 6)
                        {
                            int expectedLength = buffer[3];
                            if (buffer.Count == expectedLength)
                            {
                                byte[] telegram = buffer.ToArray();
                                buffer.Clear();

                                string hexString = BitConverter.ToString(telegram).Replace("-", " ");
                                Debug.WriteLine("Listener: " + hexString);

                                _telegramQueue.Enqueue(telegram);
                                _telegramAvailable.Release();
                                MonetoringManager.AnalyzeResponse(telegram);
                            }
                            else if (buffer.Count > expectedLength)
                            {
                                // Ungültiger Puffer – zurücksetzen
                                buffer.Clear();
                            }
                        }
                    }
                }
                catch (TimeoutException)
                {
                    // Timeout – nichts tun
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Listener-Fehler: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Sendet ein Telegramm und wartet synchron auf ein passendes Antworttelegramm,
        /// das anhand der erwarteten Datenbytes (ab Index 4 und 5) bestimmt wird.
        /// </summary>
        public byte[] SendCommand(byte[] data)
        {
            lock (_lock)
            {
                try
                {
                    // Telegramm erzeugen: Header, Länge, Daten, CRC, Footer
                    byte[] command = new byte[data.Length + 6];
                    command[0] = 0x95;
                    command[1] = 0x9A;
                    command[2] = 0x00;
                    command[3] = (byte)(data.Length + 6);
                    Array.Copy(data, 0, command, 4, data.Length);
                    command[data.Length + 4] = HseCom.CalculateCRC(data);
                    command[data.Length + 5] = 0x85;

                    Debug.WriteLine("Zu sendendes Telegramm:");
                    Debug.WriteLine(BitConverter.ToString(command).Replace("-", " "));

                    // Vor dem Senden werden alle alten Telegramme aus der Queue entfernt
                    while (_telegramQueue.TryDequeue(out _)) { }

                    _serialPort.Write(command, 0, command.Length);
                    Debug.WriteLine("Befehl gesendet, warte auf Antwort...");

                    // Beispielhafte Ableitung:
                    // Bei einem gesendeten Telegramm mit data[0] = 0x03 und data[1] = 0x01
                    // erwarten wir, dass an Position 4 der gleiche Wert (0x03)
                    // und an Position 5 der Wert (0x01 + 0x10 = 0x11) steht.
                    byte expectedByte1 = data[0];
                    byte expectedByte2 = (byte)(data[1] + 0x10);

                    // Warte insgesamt bis zu 2000 ms auf ein passendes Telegramm
                    int timeout = 2000;
                    DateTime start = DateTime.Now;
                    while ((DateTime.Now - start).TotalMilliseconds < timeout)
                    {
                        if (_telegramAvailable.Wait(100)) // Warte in 100-ms-Intervallen
                        {
                            while (_telegramQueue.TryDequeue(out byte[] telegram))
                            {
                                if (telegram.Length >= 6 &&
                                    telegram[4] == expectedByte1 &&
                                    telegram[5] == expectedByte2)
                                {
                                    return telegram;
                                }
                            }
                        }
                    }
                    Debug.WriteLine("Timeout beim Warten auf Antwort.");
                    return null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return null;
                }
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.Close();
                    Debug.WriteLine("Serielle Verbindung geschlossen.");
                }
                _cancellationTokenSource.Cancel();
            }
        }
    }

    public class HseCom
    {
        /// <summary>
        /// Berechnet die CRC (Prüfsumme) für die Daten.
        /// </summary>
        public static byte CalculateCRC(byte[] data)
        {
            byte crc = 0x00;
            foreach (byte b in data)
            {
                crc ^= b;
            }
            return (byte)~crc;
        }

        /// <summary>
        /// Wandelt eine Zahl in ein Array von Ziffern um.
        /// </summary>
        public static int[] IntToArray(int number)
        {
            string numberString = number.ToString();
            int[] digits = new int[numberString.Length];
            for (int i = 0; i < numberString.Length; i++)
            {
                digits[i] = int.Parse(numberString[i].ToString());
            }
            return digits;
        }

        /// <summary>
        /// Sendet ein HSE-Telegramm über die persistente serielle Verbindung.
        /// </summary>
        public static byte[] SendHseCommand(byte[] data)
        {
            return SerialPortManager.Instance.SendCommand(data);
        }

        public static int SendHse(int Art)
        {
            // Art 1001: Berechnung der Etagenanzahl
            if (Art == 1001)
            {
                try
                {
                    byte[] floor = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x00, 0x00, 0x03 });
                    if (floor == null || floor.Length <= 10)
                        return 505;
                    int bootFloor = floor[10];
                    floor = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x01, 0x00, 0x03 });
                    if (floor == null || floor.Length <= 10)
                        return 505;
                    int topFloor = floor[10];
                    return (topFloor - bootFloor) + 1;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (1001): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 1002: Berechnung der aktuellen Etage aus Boot- und aktueller Etage
            if (Art == 1002)
            {
                try
                {
                    byte[] bottomfloor = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
                    if (bottomfloor == null || bottomfloor.Length <= 11)
                        return 505;
                    int topFloor = bottomfloor[10];
                    byte[] bottomfloorName = new byte[2];
                    bottomfloorName[0] = bottomfloor[11];
                    bottomfloorName[1] = bottomfloor[10];
                    string asciiString = Encoding.ASCII.GetString(bottomfloorName);
                    int bootFloor = Convert.ToInt32(asciiString);
                    byte[] currentfloor = SendHseCommand(new byte[] { 0x03, 0x01, 0x21, 0x01, 0x01, 0x05 });
                    if (currentfloor == null || currentfloor.Length <= 10)
                        return 505;
                    int IcurrentFloor = currentfloor[10];
                    IcurrentFloor += bootFloor;
                    return IcurrentFloor;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (1002): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 1003: Kombination von SK1 bis SK4
            if (Art == 1003)
            {
                try
                {
                    byte[] inputByte = SendHseCommand(new byte[] { 0x03, 0x01, 0x21, 0x02, 0x00, 0x05 });
                    if (inputByte == null || inputByte.Length <= 10)
                        return 505;
                    byte sk = inputByte[10];
                    bool[] Sk = new bool[4];
                    for (int i = 0; i < 4; i++)
                    {
                        Sk[i] = (sk & (1 << i)) != 0;
                    }
                    int result = (Sk[0] ? 1000 : 0) + (Sk[1] ? 100 : 0) + (Sk[2] ? 10 : 0) + (Sk[3] ? 1 : 0);
                    Debug.WriteLine($"Kombinierter Wert von SK1 bis SK4: {result}");
                    return result;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (1003): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 1004: Aktuelle Etage (ohne Bootaddition)
            if (Art == 1004)
            {
                try
                {
                    byte[] bottomfloor = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
                    if (bottomfloor == null || bottomfloor.Length <= 11)
                        return 505;
                    int topFloor = bottomfloor[10];
                    byte[] bottomfloorName = new byte[2];
                    bottomfloorName[0] = bottomfloor[11];
                    bottomfloorName[1] = bottomfloor[10];
                    string asciiString = Encoding.ASCII.GetString(bottomfloorName);
                    int bootFloor = Convert.ToInt32(asciiString);
                    byte[] currentfloor = SendHseCommand(new byte[] { 0x03, 0x01, 0x21, 0x01, 0x01, 0x05 });
                    if (currentfloor == null || currentfloor.Length <= 10)
                        return 505;
                    int IcurrentFloor = currentfloor[10];
                    return IcurrentFloor;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (1004): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 1005: Zustand (z. B. Fehlerzustand) auslesen
            if (Art == 1005)
            {
                try
                {
                    byte[] zustand = SendHseCommand(new byte[] { 0x03, 0x01, 0x20, 0xFF, 0x00, 0x05 });
                    if (zustand == null || zustand.Length <= 10)
                        return 505;
                    return zustand[10];
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (1005): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 1006: Tür 1 Zustand
            if (Art == 1006)
            {
                try
                {
                    byte[] tuer1 = SendHseCommand(new byte[] { 0x03, 0x01, 0x63, 0x01, 0x01, 0x06 });
                    if (tuer1 == null || tuer1.Length <= 11)
                        return 505;
                    int zustand1 = tuer1[11];
                    return zustand1;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (1006): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 1016: Tür 1 Zustand alternative Abfrage
            if (Art == 1016)
            {
                try
                {
                    byte[] tuer1 = SendHseCommand(new byte[] { 0x03, 0x01, 0x63, 0x01, 0x02, 0x06 });
                    if (tuer1 == null || tuer1.Length <= 11)
                        return 505;
                    int zustand1 = tuer1[11];
                    return zustand1;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (1016): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 2001: Datum/Uhrzeit abfragen
            if (Art == 2001)
            {
                try
                {
                    byte[] date = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x00, 0x00, 0x03 });
                    if (date == null || date.Length <= 15)
                        return 505;
                    int year = date[10];
                    int month = date[11];
                    int day = date[12];
                    int hour = date[13];
                    int minute = date[14];
                    int second = date[15];
                    // Hier könnte man auch das Datum zusammenbauen. Fürs Beispiel wird 0 zurückgegeben.
                    return 0;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (2001): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 3001: Temperatur abfragen
            if (Art == 3001)
            {
                try
                {
                    byte[] date = SendHseCommand(new byte[] { 0x0C, 0x03, 0x26, 0x48, 0x00, 0x02 });
                    if (date == null || date.Length <= 10)
                        return 505;
                    int temp = date[10];
                    return temp;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (3001): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }
            

            // Art 8002: Monetoring

            if (Art == 8002)
            {
                try
                {
                    byte[] date = SendHseCommand(new byte[] { 0x05, 0x01 });
                    if (date == null || date.Length <= 10)
                        return 505;
                    int temp = date[10];
                    return temp;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (3001): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            return 404;
        }
    }
}
