using Avalonia.Controls;
using System;
using System.IO.Ports;
using Avalonia.Interactivity;
using System.Threading;
using System.Diagnostics;
using HSED_2_0;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Media;
using System.Text.Json;

namespace HSED_2._0
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource _cancellationTokenSource;
        bool NavBarStatus = false;
        private DispatcherTimer _blinkTimer;
        private bool _isGreen = false;
        public int gesamteFloors = HseCom.SendHse(1001);

        public MainWindow()
        {
            InitializeComponent();
            HseConnect();
            SetupBlinkTimer();
            _cancellationTokenSource = new CancellationTokenSource();
            StartPeriodicUpdateO(TimeSpan.FromSeconds(1), _cancellationTokenSource.Token);
            StartPeriodicUpdate(TimeSpan.FromSeconds(30), _cancellationTokenSource.Token);
            StartPeriodicUpdateBlink(TimeSpan.FromSeconds(4), _cancellationTokenSource.Token);
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

        private async void AnimateProgressBar(int targetValue)
        {
            var progressBar = this.FindControl<ProgressBar>("EtageProgressBar");
            if (progressBar == null) return;
            double currentValue = progressBar.Value;
            double step = 0.1 * Math.Sign(targetValue - currentValue);
            while (Math.Abs(targetValue - currentValue) > Math.Abs(step))
            {
                currentValue += step;
                progressBar.Value = currentValue;
                await Task.Delay(15);
            }
            progressBar.Value = targetValue;
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
            _blinkTimer.Tick -= ToggleColor;
            _cancellationTokenSource.Cancel();
            base.OnClosed(e);
        }

        private void HseConnect()
        {
            EtageProgressBar.Maximum = gesamteFloors - 1;
            int temp = HseCom.SendHse(3001);
            Temp.Text = temp.ToString() + "°C";
            int currentfloor = HseCom.SendHse(1002);
            Etage.Text = currentfloor.ToString();
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
            var skBorders = new Border[] { SK1, SK2, SK3, SK4 };
            int GanzeSK = HseCom.SendHse(1003);
            int[] SK = HseCom.IntToArray(GanzeSK);
            for (int i = 0; i < SK.Length; i++)
            {
                if (SK[i] == 0)
                {
                    skBorders[i].Background = new SolidColorBrush(Colors.Red);
                }
                else
                {
                    skBorders[i].Background = new SolidColorBrush(Colors.GreenYellow);
                }
            }

            int currentfloor = HseCom.SendHse(1002);
            if (currentfloor == 505 || currentfloor == 404)
            {
                return;
            }
            Etage.Text = currentfloor.ToString();

            int AZustand = HseCom.SendHse(1005);
            if (AZustand == 505 || AZustand == 404)
            {
                return;
            }

            switch (AZustand)
            {
                case 4:
                    Zustand.Text = "Stillstand";
                    Zustand.Foreground = new SolidColorBrush(Colors.White);
                    break;
                case 5:
                    Zustand.Text = "Fährt";
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
            EtageProgressBar.Value = currentfloor + 1;
        }

        private void HseUpdated()
        {
            int temp = HseCom.SendHse(3001);
            if (temp == 505 || temp == 404)
            {
                return;
            }
            Temp.Text = temp.ToString() + "°C";
        }

        private void Button_Click_Settings(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                string buttonTag = button.Tag?.ToString();
                if (buttonTag == "Menu")
                {
                    if (NavBarStatus == false)
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
                            var newWindowTestrufe = new Testrufe();
                            newWindowTestrufe.Show();
                            this.Close();
                            break;
                        case "Codes":
                            var newWindowCode = new Code();
                            newWindowCode.Show();
                            break;
                    }
                }
            }
        }
    }
}
