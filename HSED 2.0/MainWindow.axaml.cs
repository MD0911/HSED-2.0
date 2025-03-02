using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Media;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HSED_2_0; // Enth‰lt MonetoringManager und HseCom
using HSED_2_0.ViewModels;

namespace HSED_2._0
{
    public partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }
        public MainViewModel ViewModel { get; }
        private MonetoringManager _monetoringManager;
        private CancellationTokenSource _cancellationTokenSource;
        bool NavBarStatus = false;
        private DispatcherTimer _blinkTimer;
        private DispatcherTimer _floorTimer;
        private bool _isGreen = false;
        public int gesamteFloors = HseCom.SendHse(1001);
        private CancellationTokenSource _heartbeatCancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();
            this.Position = new Avalonia.PixelPoint(100, 100);
            Instance = this; // Speichert die Instanz
            ViewModel = new MainViewModel();
            DataContext = ViewModel;
            HseConnect();
            SetupBlinkTimer();
            StartFloorTimer(); // Startet den Timer, der die Floor-Anzeige regelm‰ﬂig updatet
            _cancellationTokenSource = new CancellationTokenSource();
            StartPeriodicUpdateO(TimeSpan.FromSeconds(1), _cancellationTokenSource.Token);
            StartPeriodicUpdate(TimeSpan.FromSeconds(30), _cancellationTokenSource.Token);
            StartPeriodicUpdateBlink(TimeSpan.FromSeconds(4), _cancellationTokenSource.Token);
        }

        /// <summary>
        /// Aktualisiert die Anzeige der aktuellen Etage via Binding.
        /// </summary>
        public void DisplayFloor()
        {
            Etage.Text = ViewModel.CurrentFloor.ToString();
            EtageProgressBar.Value = ViewModel.CurrentFloor + 1;
        }

        /// <summary>
        /// Aktualisiert den SK-Zustand in der UI.
        /// </summary>
        public void UpdateSK(int skValue)
        {
            // Beispiel: Setze alle SK-Elemente auf Rot, wenn skValue 0 ist, sonst auf GreenYellow.
            SK1.Background = skValue == 0 ? new SolidColorBrush(Colors.Red) : new SolidColorBrush(Colors.GreenYellow);
            SK2.Background = skValue == 0 ? new SolidColorBrush(Colors.Red) : new SolidColorBrush(Colors.GreenYellow);
            SK3.Background = skValue == 0 ? new SolidColorBrush(Colors.Red) : new SolidColorBrush(Colors.GreenYellow);
            SK4.Background = skValue == 0 ? new SolidColorBrush(Colors.Red) : new SolidColorBrush(Colors.GreenYellow);
        }

        /// <summary>
        /// Startet einen DispatcherTimer, der alle 50 ms die Floor-Anzeige aktualisiert.
        /// </summary>
        private void StartFloorTimer()
        {
            _floorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _floorTimer.Tick += (sender, e) => DisplayFloor();
            _floorTimer.Start();
        }

        private void SetupBlinkTimer()
        {
            _blinkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            _blinkTimer.Tick += ToggleColor;
            _blinkTimer.Start();
        }

        // Signatur: (object, EventArgs)
        private void ToggleColor(object sender, EventArgs e)
        {
            Temp.Foreground = _isGreen
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Colors.GreenYellow);
            _isGreen = !_isGreen;
        }

        private async void StartPeriodicUpdateO(TimeSpan interval, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    HseUpdatedO();
                    SerialPortManager.Instance.Open();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
                try
                {
                    await Task.Delay(interval, token);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }

        private async void StartPeriodicUpdateBlink(TimeSpan interval, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    SK();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
                try
                {
                    await Task.Delay(interval, token);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }

        private async void StartPeriodicUpdate(TimeSpan interval, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    HseUpdated();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
                try
                {
                    await Task.Delay(interval, token);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _blinkTimer.Stop();
            _floorTimer?.Stop();
            _blinkTimer.Tick -= ToggleColor;
            _cancellationTokenSource.Cancel();
            _heartbeatCancellationTokenSource?.Cancel();
            base.OnClosed(e);
        }

        private void HseConnect()
        {
            EtageProgressBar.Maximum = gesamteFloors - 1;

            // Temperatur abfragen und anzeigen
            int temp = HseCom.SendHse(3001);
            Temp.Text = temp.ToString() + "∞C";

            // Initialisiere einmalig die statischen Floor-Parameter:
            MonetoringManager.startMonetoring();

            // Initiales Abfragen der aktuellen Etage (Art 1002)
            int currentFloor = HseCom.SendHse(1002);
            // Hier aktualisieren wir direkt das ViewModel:
            ViewModel.CurrentFloor = currentFloor;
            Debug.WriteLine("Initialer Etagenwert: " + currentFloor);

            // Starte den Monitoring-Manager
            _monetoringManager = new MonetoringManager();
            _monetoringManager.Start();
        }


        private void SK()
        {
            Zustand.Foreground = new SolidColorBrush(Colors.Gray);
            SK1.Background = new SolidColorBrush(Colors.Gray);
            SK2.Background = new SolidColorBrush(Colors.Gray);
            SK3.Background = new SolidColorBrush(Colors.Gray);
            SK4.Background = new SolidColorBrush(Colors.Gray);
        }

        private void HseUpdatedO()
        {
            // Aktualisiere den SK-Zustand basierend auf HseCom.SendHse(1003)
            var skBorders = new Border[] { SK1, SK2, SK3, SK4 };
            int GanzeSK = HseCom.SendHse(1003);
            int[] skValues = HseCom.IntToArray(GanzeSK);
            for (int i = 0; i < skValues.Length; i++)
            {
                skBorders[i].Background = skValues[i] == 0 
                    ? new SolidColorBrush(Colors.Red) 
                    : new SolidColorBrush(Colors.GreenYellow);
            }

            // Aktualisiere den A-Zustand basierend auf HseCom.SendHse(1005)
            int AZustand = HseCom.SendHse(1005);
            if (AZustand == 505 || AZustand == 404)
                return;
            Dispatcher.UIThread.Post(() =>
            {
                switch (AZustand)
                {
                    case 4:
                        Zustand.Text = "Stillstand";
                        Zustand.Foreground = new SolidColorBrush(Colors.White);
                        break;
                    case 5:
                        //MonetoringManager.animationValidator
                        Zustand.Text = "F‰hrt";
                        Zustand.Foreground = new SolidColorBrush(Colors.GreenYellow);
                        break;
                    case 6:
                        Zustand.Text = "Einfahrt";
                        Zustand.Foreground = new SolidColorBrush(Colors.Yellow);
                        break;
                    case 17:
                        Zustand.Text = "SK Fehlt";
                        Zustand.Foreground = new SolidColorBrush(Colors.Red);
                        break;
                }
            });
        }

        private void HseUpdated()
        {
            int temp = HseCom.SendHse(3001);
            if (temp == 505 || temp == 404)
                return;
            Temp.Text = temp.ToString() + "∞C";
        }

        private void Button_Click_Settings(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                string buttonTag = button.Tag?.ToString();
                if (buttonTag == "Menu")
                {
                    if (!NavBarStatus)
                    {
                        NavBar.Width += 100;
                        StackPanelNavBar.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                        StackPanelNavBar.Margin = new Avalonia.Thickness(10, 25, 0, 0);
                        SettingsText.IsVisible = true;
                        ButtonSettings.Width = 100;
                        SettingsText2.IsVisible = true;
                        ButtonSettings2.Width = 100;
                        SettingsText3.IsVisible = true;
                        ButtonSettings3.Width = 100;
                        SettingsText4.IsVisible = true;
                        ButtonSettings4.Width = 100;
                        SettingsText5.IsVisible = true;
                        ButtonSettings5.Width = 100;
                        Overlap.IsVisible = true;
                        SettingsText6.IsVisible = true;
                        ButtonSettings6.Width = 100;
                        SettingsText7.IsVisible = true;
                        ButtonSettings7.Width = 100;
                        NavBarStatus = true;
                    }
                    else
                    {
                        NavBar.Width -= 100;
                        StackPanelNavBar.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
                        StackPanelNavBar.Margin = new Avalonia.Thickness(0, 25, 0, 0);
                        SettingsText.IsVisible = false;
                        ButtonSettings.Width = 50;
                        SettingsText2.IsVisible = false;
                        ButtonSettings2.Width = 50;
                        SettingsText3.IsVisible = false;
                        ButtonSettings3.Width = 50;
                        SettingsText4.IsVisible = false;
                        ButtonSettings4.Width = 50;
                        SettingsText5.IsVisible = false;
                        ButtonSettings5.Width = 50;
                        SettingsText6.IsVisible = false;
                        ButtonSettings6.Width = 50;
                        SettingsText7.IsVisible = false;
                        ButtonSettings7.Width = 50;
                        Overlap.IsVisible = false;
                        NavBarStatus = false;
                    }
                }
                else
                {
                    switch (buttonTag)
                    {
                        case "Settings":
                            var newWindowSettings = new Settings();
                            newWindowSettings.Show();
                            break;
                        case "Testrufe":
                            var newWindowTestrufe = new TestrufeNeu();
                            newWindowTestrufe.Show();
                            this.Close();
                            break;
                        case "Codes":
                            var newWindowCode = new Code();
                            newWindowCode.Show();
                            break;
                        case "SelfDia":
                            var newWindowDia = new LiveViewAnimationSimulation();
                            newWindowDia.Show();
                           this.Close();

                            break;
                        case "Ansicht":
                            TerminalManager.terminalActive = true;
                            var newWindowAnsicht = new Terminal();
                            newWindowAnsicht.Show();
                            break;
                    }
                }
            }
        }
    }
}
