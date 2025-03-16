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
                // Entferne Schließ-Optionen
                SystemDecorations = SystemDecorations.None,
                Content = new StackPanel
                {
                    Margin = new Thickness(10),
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Keine Verbindung zur HSE.",
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                        },
                        // Optional: Ein Hinweis, dass der Dialog nicht geschlossen werden kann.
                        new TextBlock
                        {
                            Text = "Bitte warten...",
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            FontStyle = Avalonia.Media.FontStyle.Italic
                        },
                        new TextBlock
                        {
                            Text = "Eine Verbindung wird ",
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            FontStyle = Avalonia.Media.FontStyle.Italic
                        },
                        new TextBlock
                        {
                            Text = "automatisch versucht herzustellen.",
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            FontStyle = Avalonia.Media.FontStyle.Italic
                        }

                    }
                }
            };

            // Verhindere, dass der Benutzer das Fenster schließt (z. B. per Alt-F4)
            errorDialog.Closing += (s, e) =>
            {
                // Abbrechen, falls der Fehlerzustand noch besteht
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

        // Alte Telegramme löschen
        while (_telegramQueue.TryDequeue(out _)) { }

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
        List<byte> buffer = new List<byte>();
        while (!token.IsCancellationRequested)
        {
            try
            {
                int byteRead = _serialPort.ReadByte();
                if (byteRead >= 0)
                {
                    byte b = (byte)byteRead;
                    buffer.Add(b);

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
                            //MonetoringManager.AnalyzeResponse(telegram);
                            TerminalManager.AnalyzeResponse(telegram);
                            var tp = new TelegramProcessor();
                            tp.ProcessTelegram(telegram);
                            
                        }
                        else if (buffer.Count > expectedLength)
                        {
                            buffer.Clear();
                        }
                    }
                }
            }
            catch (TimeoutException)
            {
                // Timeout ignorieren
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Listener-Fehler: " + ex.Message);
            }
        }
    }

    public byte[] SendCommand(byte[] data)
    {
        lock (_lock)
        {
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

                while (_telegramQueue.TryDequeue(out _)) { }

                _serialPort.Write(command, 0, command.Length);
                Debug.WriteLine("Befehl gesendet, warte auf Antwort...");

                byte expectedByte1 = data[0];
                byte expectedByte2 = (byte)(data[1] + 0x10);

                int timeout = 2000;
                DateTime start = DateTime.Now;
                while ((DateTime.Now - start).TotalMilliseconds < timeout)
                {
                    if (_telegramAvailable.Wait(100))
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
                ShowConnectionErrorDialog();
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
