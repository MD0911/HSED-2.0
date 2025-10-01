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
    private DispatcherTimer _reconnectTimer;
    private int _remainingSeconds = 30;
    private TextBlock _countdownText;
    private TextBlock _hintText;

   

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
        Dispatcher.UIThread.Post(async () =>
        {
            if (_connectionErrorDialog == null)
            {
                var errorDialog = new Window
                {
                    Title = "Verbindungsfehler",
                    Width = 360,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    CanResize = false,
                    SystemDecorations = SystemDecorations.None,
                    ShowInTaskbar = false,
                    Topmost = true
                };

                var stackPanel = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 12,
                };

                stackPanel.Children.Add(new TextBlock
                {
                    Text = "Verbindung mit der HSE prüfen",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    FontSize = 16,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold
                });

                _hintText = new TextBlock
                {
                    Text = "Keine Verbindung verfügbar.",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                };
                stackPanel.Children.Add(_hintText);

                _countdownText = new TextBlock
                {
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    FontStyle = Avalonia.Media.FontStyle.Italic
                };
                stackPanel.Children.Add(_countdownText);

                var reconnectButton = new Button
                {
                    Content = "Jetzt erneut versuchen",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Padding = new Thickness(12, 6)
                };
                reconnectButton.Click += (sender, e) =>
                {
                    StopReconnectCountdown();
                    AttemptReconnect();
                };
                stackPanel.Children.Add(reconnectButton);

                errorDialog.Content = stackPanel;

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
            }
            else
            {
                if (!_connectionErrorDialog.IsVisible)
                {
                    _connectionErrorDialog.Show();
                }

                _connectionErrorDialog.Activate();
            }

            RestartReconnectCountdown();
        });
    }

    private void RestartReconnectCountdown()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_reconnectTimer == null)
            {
                _reconnectTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _reconnectTimer.Tick += OnReconnectTimerTick;
            }

            _remainingSeconds = 30;
            UpdateCountdownText();

            if (!_reconnectTimer.IsEnabled)
            {
                _reconnectTimer.Start();
            }
        });
    }

    private void StopReconnectCountdown()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_reconnectTimer != null && _reconnectTimer.IsEnabled)
            {
                _reconnectTimer.Stop();
            }
        });
    }

    private void OnReconnectTimerTick(object sender, EventArgs e)
    {
        if (_remainingSeconds > 0)
        {
            _remainingSeconds--;
            UpdateCountdownText();
        }

        if (_remainingSeconds <= 0)
        {
            StopReconnectCountdown();
            AttemptReconnect();
        }
    }

    private void UpdateCountdownText()
    {
        if (_countdownText != null)
        {
            _countdownText.Text = $"Nächster Versuch in {_remainingSeconds:D2} Sekunden.";
        }
    }

    private void UpdateHintText(string text)
    {
        if (_hintText != null)
        {
            _hintText.Text = text;
        }
    }

    private bool _isAttemptingReconnect;

    private void AttemptReconnect()
    {
        if (_isAttemptingReconnect)
        {
            return;
        }

        _isAttemptingReconnect = true;
        UpdateHintText("Verbindung wird aufgebaut...");

        try
        {
            Open();

            if (_serialPort == null || !_serialPort.IsOpen)
            {
                UpdateHintText("Keine Verbindung verfügbar.");
                RestartReconnectCountdown();
            }
        }
        finally
        {
            _isAttemptingReconnect = false;
        }
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
                    StopReconnectCountdown();
                    _hintText = null;
                    _countdownText = null;
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
        if (_serialPort == null || !_serialPort.IsOpen)
        {
            ShowConnectionErrorDialog();
            RestartReconnectCountdown();
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
                    RestartReconnectCountdown();
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
                if (_serialPort == null || !_serialPort.IsOpen)
                {
                    ShowConnectionErrorDialog();
                    RestartReconnectCountdown();
                    Thread.Sleep(500);
                    continue;
                }

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
            catch (InvalidOperationException ioEx)
            {
                Debug.WriteLine("Listener-Fehler (ungültiger Zustand): " + ioEx.Message);
                ShowConnectionErrorDialog();
                RestartReconnectCountdown();
                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Listener-Fehler: " + ex.Message);
                ShowConnectionErrorDialog();
                RestartReconnectCountdown();
                Thread.Sleep(500);
            }
        }
    }

    public byte[] SendCommand(byte[] data)
    {
        lock (_lock)
        {
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen)
                {
                    ShowConnectionErrorDialog();
                    RestartReconnectCountdown();
                    return null;
                }

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
                RestartReconnectCountdown();
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
