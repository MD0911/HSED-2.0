using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;

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

        private SerialPortManager()
        {
            _serialPort = new SerialPort("COM3", 38400, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 100,
                NewLine = "\r\n"
            };

            // Initiale Öffnung der Verbindung
            Open();
        }

        public static SerialPortManager Instance => _instance;

        /// <summary>
        /// Öffnet die serielle Verbindung, falls sie nicht bereits geöffnet ist.
        /// </summary>
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

        public byte[] SendCommand(byte[] data)
        {
            lock (_lock)
            {
                try
                {
                    // Erzeuge das Telegramm: Header, Länge, Daten, CRC und Footer
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

                    _serialPort.Write(command, 0, command.Length);
                    Debug.WriteLine("Befehl gesendet, warte auf Antwort...");

                    // Lese Antwort (mit Timeout)
                    byte[] buffer = new byte[1024];
                    int totalBytesRead = 0;
                    try
                    {
                        int bytesRead;
                        do
                        {
                            bytesRead = _serialPort.Read(buffer, totalBytesRead, buffer.Length - totalBytesRead);
                            totalBytesRead += bytesRead;
                        }
                        while (bytesRead > 0 && totalBytesRead < buffer.Length);
                    }
                    catch (TimeoutException)
                    {
                        Debug.WriteLine("Lesen der Daten aufgrund eines Zeitüberschreitungsfehlers abgebrochen.");
                    }

                    if (totalBytesRead > 0)
                    {
                        byte[] receivedData = new byte[totalBytesRead];
                        Array.Copy(buffer, receivedData, totalBytesRead);
                        return receivedData;
                    }
                    else
                    {
                        Debug.WriteLine("Keine Daten empfangen.");
                        return null;
                    }
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
            if (Art == 1001)
            {
                try
                {
                    byte[] floor = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x00, 0x00, 0x03 });
                    int bootFloor = floor[10];
                    floor = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x01, 0x00, 0x03 });
                    int topFloor = floor[10];
                    return (topFloor - bootFloor) + 1;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return 505;
                }
            }

            if (Art == 1002)
            {
                try
                {
                    byte[] bottomfloor = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
                    int topFloor = bottomfloor[10];
                    byte[] bottomfloorName = new byte[2];
                    bottomfloorName[0] = bottomfloor[11];
                    bottomfloorName[1] = bottomfloor[10];
                    string asciiString = Encoding.ASCII.GetString(bottomfloorName);
                    int bootFloor = Convert.ToInt32(asciiString);
                    byte[] currentfloor = SendHseCommand(new byte[] { 0x03, 0x01, 0x21, 0x01, 0x01, 0x05 });
                    int IcurrentFloor = currentfloor[10];
                    IcurrentFloor = IcurrentFloor + bootFloor;
                    return IcurrentFloor;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return 505;
                }
            }

            if (Art == 1003)
            {
                try
                {
                    byte[] inputByte = SendHseCommand(new byte[] { 0x03, 0x01, 0x21, 0x02, 0x00, 0x05 });
                    byte sk = inputByte[10];
                    bool[] Sk = new bool[4];
                    for (int i = 0; i < 4; i++)
                    {
                        Sk[i] = (sk & (1 << i)) != 0;
                    }
                    int result = (Sk[0] ? 1000 : 0) + (Sk[1] ? 100 : 0) + (Sk[2] ? 10 : 0) + (Sk[3] ? 1 : 0);
                    Console.WriteLine($"Kombinierter Wert von SK1 bis SK4: {result}");
                    return result;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return 505;
                }
            }

            if (Art == 1004)
            {
                try
                {
                    byte[] bottomfloor = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
                    int topFloor = bottomfloor[10];
                    byte[] bottomfloorName = new byte[2];
                    bottomfloorName[0] = bottomfloor[11];
                    bottomfloorName[1] = bottomfloor[10];
                    string asciiString = Encoding.ASCII.GetString(bottomfloorName);
                    int bootFloor = Convert.ToInt32(asciiString);
                    byte[] currentfloor = SendHseCommand(new byte[] { 0x03, 0x01, 0x21, 0x01, 0x01, 0x05 });
                    int IcurrentFloor = currentfloor[10];
                    return IcurrentFloor;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return 505;
                }
            }

            if (Art == 1005)
            {
                try
                {
                    byte[] zustand = SendHseCommand(new byte[] { 0x03, 0x01, 0x20, 0xFF, 0x00, 0x05 });
                    return zustand[10];
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return 505;
                }
            }

            if (Art == 1006)
            {
                try
                {
                    byte[] tuer1 = SendHseCommand(new byte[] { 0x03, 0x01, 0x63, 0x01, 0x01, 0x06 });
                    int zustand1 = tuer1[11];
                    return zustand1;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return 505;
                }
            }

            if (Art == 1016)
            {
                try
                {
                    byte[] tuer1 = SendHseCommand(new byte[] { 0x03, 0x01, 0x63, 0x01, 0x02, 0x06 });
                    int zustand1 = tuer1[11];
                    return zustand1;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return 505;
                }
            }

            if (Art == 2001)
            {
                try
                {
                    byte[] date = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x00, 0x00, 0x03 });
                    int year = date[10];
                    int month = date[11];
                    int day = date[12];
                    int hour = date[13];
                    int minute = date[14];
                    int second = date[15];
                    return 0;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return 505;
                }
            }

            if (Art == 3001)
            {
                try
                {
                    byte[] date = SendHseCommand(new byte[] { 0x0C, 0x03, 0x26, 0x48, 0x00, 0x02 });
                    int temp = date[10];
                    return temp;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return 505;
                }
            }

            return 404;
        }
    }
}
