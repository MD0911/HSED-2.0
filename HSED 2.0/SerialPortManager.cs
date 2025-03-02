using HSED_2_0;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading.Tasks;
using System.Threading;
using System;

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

    /// <summary>
    /// Sendet ein Telegramm ohne auf eine Antwort zu warten.
    /// </summary>
    public async Task SendWithoutResponse(byte[] data)
    {
        byte[] command = new byte[data.Length + 6];
        command[0] = 0x95;
        command[1] = 0x9A;
        command[2] = 0x00;
        command[3] = (byte)(data.Length + 6);
        Array.Copy(data, 0, command, 4, data.Length);
        command[data.Length + 4] = HseCom.CalculateCRC(data);
        command[data.Length + 5] = 0x85;

        Debug.WriteLine("Sende Telegramm ohne Antwort zu erwarten:");
        Debug.WriteLine(BitConverter.ToString(command).Replace("-", " "));

        // Optional: Nur wenn wirklich notwendig, alte Telegramme löschen
        while (_telegramQueue.TryDequeue(out _)) { }

        // Falls der serielle Port asynchrones Schreiben über seine BaseStream unterstützt:
        await _serialPort.BaseStream.WriteAsync(command, 0, command.Length);
    }


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
                            TerminalManager.AnalyzeResponse(telegram);
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