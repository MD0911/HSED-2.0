using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using HSED_2_0;
using HSED_2._0;
using Avalonia.Controls.ApplicationLifetimes;
using Material.Styles.Controls;
using Avalonia.Controls.Platform;
using Microsoft.Extensions.Configuration;
using System.IO;


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
    private bool firstStart = true;
    public static SerialPortManager Instance => _instance;

    // Statische Referenz für den Fehlerdialog

    private static Window _connectionErrorDialog = null;
    // Klasse: SerialPortManager (neue Felder)
    private Task _processingTask;

    // Klasse: SerialPortManager (neue Felder)
    private readonly ConcurrentQueue<byte[]> _responseQueue = new ConcurrentQueue<byte[]>();
    private readonly SemaphoreSlim _responseAvailable = new SemaphoreSlim(0);

    private readonly TelegramProcessor _tp = new TelegramProcessor();
    // Klasse: SerialPortManager
    private static int _waitingForResponse = 0;




    private SerialPortManager()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("config.json", optional: false, reloadOnChange: true)
            .Build();

        string serialPort = config["SerialSettings:SerialPort"];
        int serialBaudrate = int.Parse(config["SerialSettings:SerialBaudrate"]);

        _serialPort = new SerialPort(serialPort, serialBaudrate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = SerialPort.InfiniteTimeout,   // wichtig: keine TimeoutException mehr
            WriteTimeout = 2000,
            NewLine = "\r\n"
        };

        Open();

        _cancellationTokenSource = new CancellationTokenSource();

        // Listener liest nur Bytes und baut Telegramme
        _listeningTask = Task.Run(() => Listen(_cancellationTokenSource.Token));

        // Verarbeitung getrennt, damit Listener nicht blockiert
        _processingTask = Task.Run(() => ProcessTelegrams(_cancellationTokenSource.Token));
    }


    /// <summary>
    /// Zeigt einen persistierenden Fehlerdialog an, falls keine Verbindung zur HSE besteht.
    /// Der Dialog wird nur einmal angezeigt und kann vom Benutzer nicht geschlossen werden.
    /// </summary>
    private void ShowConnectionErrorDialog()
    {
        // Falls der Dialog schon offen ist, nichts tun
        if (_connectionErrorDialog != null)
            return;

        Dispatcher.UIThread.Post(async () =>
        {
            var errorDialog = new Window
            {
                Title = "Verbindungsfehler",
                Width = 300,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                CanResize = false,
                SystemDecorations = SystemDecorations.None
            };

            var stackPanel = new StackPanel
            {
                Margin = new Thickness(10),
                Spacing = 10,
            };

            stackPanel.Children.Add(new TextBlock
            {
                Text = "Keine Verbindung zur HSE.",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            });
            stackPanel.Children.Add(new TextBlock
            {
                Text = "Bitte warten...",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                FontStyle = Avalonia.Media.FontStyle.Italic
            });
            stackPanel.Children.Add(new TextBlock
            {
                Text = "Eine Verbindung wird alle 5 Sekunden",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                FontStyle = Avalonia.Media.FontStyle.Italic
            });
            stackPanel.Children.Add(new TextBlock
            {
                Text = "automatisch versucht herzustellen.",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                FontStyle = Avalonia.Media.FontStyle.Italic
            });

            // Button separat erstellen und das Click-Ereignis zuweisen
            var reconnectButton = new Button
            {
                Content = "Manuell verbinden",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            reconnectButton.Click += (sender, e) => Open();
            stackPanel.Children.Add(reconnectButton);

            errorDialog.Content = stackPanel;

            // Verhindere, dass der Benutzer das Fenster schließt (z. B. per Alt-F4),
            // solange keine Verbindung besteht
            errorDialog.Closing += (s, e) =>
            {
                if (_serialPort == null || !_serialPort.IsOpen)
                {
                    e.Cancel = true;
                }
            };

            _connectionErrorDialog = errorDialog;

            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var owner = lifetime?.MainWindow;
            if (owner != null && owner.IsVisible)
            {
                await errorDialog.ShowDialog(owner);
            }
            else
            {
                errorDialog.Show();
            }
        });
    }


    /// <summary>
    /// Schließt den Fehlerdialog, falls er offen ist.
    /// </summary>
   private void CloseConnectionErrorDialog()
{
    if (_connectionErrorDialog != null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_connectionErrorDialog.IsVisible)
                {
                    _connectionErrorDialog.Close();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Fehler beim Schließen des Fehlerdialogs: " + ex.Message);
            }
            finally
            {
                _connectionErrorDialog = null;
            }
        });
    }
}


    /// <summary>
    /// Sendet ein Telegramm ohne auf eine Antwort zu warten.
    /// </summary>
    /// 
    // Klasse: SerialPortManager
    // Klasse: SerialPortManager
    // Klasse: SerialPortManager
    // Klasse: SerialPortManager
    private void ProcessTelegrams(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                _telegramAvailable.Wait(token);

                while (_telegramQueue.TryDequeue(out byte[] telegram))
                {
                    // Nur wenn wirklich jemand auf eine Antwort wartet,
                    // kommt das Telegramm in die Response-Queue
                    if (Volatile.Read(ref _waitingForResponse) > 0)
                    {
                        _responseQueue.Enqueue(telegram);
                        _responseAvailable.Release();
                    }

                    // ===== ROUTING: nur relevante Parser aufrufen =====

                    // Terminal nur bei Terminal-Telegrammen
                    if (TerminalManager.terminalActive &&
                        telegram.Length >= 6 &&
                        telegram[4] == 0x01 &&
                        (telegram[5] == 0x04 || telegram[5] == 0x02))
                    {
                        TerminalManager.AnalyzeResponse(telegram);
                    }

                    // Monitoring / TelegramProcessor nur bei 0x05 0x02
                    if (telegram.Length >= 6 &&
                        telegram[4] == 0x05 &&
                        telegram[5] == 0x02)
                    {
                        _tp.ProcessTelegram(telegram);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ProcessTelegrams Fehler: " + ex.Message);
            }
        }
    }






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

       
        

        // Prüfe, ob der Port offen ist, andernfalls zeige Fehlerdialog
        if (!_serialPort.IsOpen)
        {
            ShowConnectionErrorDialog();
            return;
        }
        else
        {
            // Falls der Port wieder offen ist, schließe den Fehlerdialog
            CloseConnectionErrorDialog();
        }

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
                    Debug.WriteLine("Erster Start: " + firstStart);
                    if (!firstStart)
                    {
                        MainWindow.Instance.HseConnect();
                    }
                    else
                    {
                        firstStart = false;
                    }
                    // Bei erfolgreicher Verbindung ggf. Fehlerdialog schließen
                    CloseConnectionErrorDialog();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler beim Öffnen der seriellen Schnittstelle: {ex.Message}");
                    ShowConnectionErrorDialog();
                }
            }
            else
            {
                Debug.WriteLine("Serielle Verbindung ist bereits geöffnet oder _serialPort ist null.");
            }
        }
    }

    private void Listen(CancellationToken token)
    {
        var buffer = new List<byte>(256);

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen)
                {
                    Thread.Sleep(200);
                    continue;
                }

                int byteRead = _serialPort.ReadByte(); // blockierend, kein Timeout mehr
                if (byteRead < 0)
                    continue;

                byte b = (byte)byteRead;
                buffer.Add(b);

                // Minimaler Frame Check: Header muss 0x95 0x9A sein
                // Wenn nicht, resync: so lange schieben bis es passt
                while (buffer.Count >= 2 && (buffer[0] != 0x95 || buffer[1] != 0x9A))
                {
                    buffer.RemoveAt(0);
                }

                // Wir brauchen mindestens 6 Bytes: 2 Header, 1,1, CRC, End
                if (buffer.Count < 6)
                    continue;

                // Länge ist bei dir buffer[3]
                int expectedLength = buffer[3];

                // Schutz gegen Müllwerte
                if (expectedLength < 6 || expectedLength > 255)
                {
                    buffer.Clear();
                    continue;
                }

                // Warten bis gesamtes Telegramm im Buffer ist
                if (buffer.Count < expectedLength)
                    continue;

                // Falls mehr drin ist, schneide genau ein Telegramm ab
                byte[] telegram = buffer.GetRange(0, expectedLength).ToArray();
                buffer.RemoveRange(0, expectedLength);

                // Endbyte prüfen
                if (telegram[expectedLength - 1] != 0x85)
                {
                    // Frame kaputt, resync
                    buffer.Clear();
                    continue;
                }

                // Nicht hier verarbeiten, nur enqueuen
                _telegramQueue.Enqueue(telegram);
                _telegramAvailable.Release();
            }
            catch (InvalidOperationException)
            {
                // Port ist gerade ungültig, z.B. während Close oder Reconnect
                Thread.Sleep(200);
            }
            catch (IOException ex)
            {
                Debug.WriteLine("Listener IO Fehler: " + ex.Message);
                ShowConnectionErrorDialog();
                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Listener-Fehler: " + ex.Message);
                Thread.Sleep(200);
            }
        }
    }


    // Klasse: SerialPortManager
    // Klasse: SerialPortManager
    public byte[] SendCommand(byte[] data)
    {
        lock (_lock)
        {
            Interlocked.Increment(ref _waitingForResponse);

            try
            {
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

                if (_serialPort == null || !_serialPort.IsOpen)
                {
                    ShowConnectionErrorDialog();
                    return null;
                }

                // Alte Antwortreste entfernen
                while (_responseQueue.TryDequeue(out _)) { }
                while (_responseAvailable.CurrentCount > 0)
                    _responseAvailable.Wait(0);

                _serialPort.Write(command, 0, command.Length);
                Debug.WriteLine("Befehl gesendet, warte auf Antwort...");

                byte expectedByte1 = data[0];
                byte expectedByte2 = (byte)(data[1] + 0x10);

                int timeoutMs = 2000;
                int start = Environment.TickCount;

                while (Environment.TickCount - start < timeoutMs)
                {
                    if (_responseAvailable.Wait(100))
                    {
                        while (_responseQueue.TryDequeue(out byte[] telegram))
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
                ShowConnectionErrorDialog();
                return null;
            }
            finally
            {
                Interlocked.Decrement(ref _waitingForResponse);
            }
        }
    }



    public void Close()
    {
        lock (_lock)
        {
            try
            {
                _cancellationTokenSource?.Cancel();

                if (_serialPort != null)
                {
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }
                }

                Debug.WriteLine("Serielle Verbindung geschlossen.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Fehler beim Schließen: " + ex.Message);
            }
        }
    }

}
