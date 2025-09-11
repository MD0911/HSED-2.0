using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using Svg.Skia;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HSED_2_0;
using HSED_2_0.ViewModels;
using System.Collections.Generic;

namespace HSED_2._0
{
    public partial class MainVertical : Window
    {
        public static MainVertical Instance { get; private set; }
        public static MainViewModel MainViewModelInstance => Instance?.ViewModel;
        public MainViewModel ViewModel { get; }

        private LievViewManager _lievViewManager;
        private MonetoringManager _monetoringManager;
        private CancellationTokenSource _cancellationTokenSource;
        private CancellationTokenSource _heartbeatCancellationTokenSource;
        private DispatcherTimer _blinkTimer;
        private DispatcherTimer _floorTimer;
        private DispatcherTimer _updateTimer;
        private bool _isGreen = false;
        private bool _isLogicInitialized = false;
        private bool NavBarStatus = false;

        public static bool BZeitSchalter = false;
        public int Pos_Cal = HseCom.SendHse(10101010);
        public int gesamteFloors = HseCom.SendHse(1001);

        public static Bitmap SharedSvgBitmap { get; private set; }
        public static Bitmap SharedSvgBitmapAlternative { get; private set; }

        private TestrufeNeu _cachedTestrufeWindow;

        public MainVertical()
        {
            InitializeComponent();
            Instance = this;

            ViewModel = new MainViewModel();
            DataContext = ViewModel;

            TestrufeService.StartBackgroundUpdate();
            this.Opened += MainVertical_Opened;

            _cachedTestrufeWindow = new TestrufeNeu();
            _cachedTestrufeWindow.Hide();

            this.Position = new PixelPoint(0, 0);
        }

        private void MainVertical_Opened(object sender, EventArgs e)
        {
            this.Opened -= MainVertical_Opened;
            StartLogic();
        }

        public void StartLogic()
        {
            if (!_isLogicInitialized)
                InitializeLogic();
            ResumeLogic();
        }

        private void InitializeLogic()
        {
            HseConnect();
            MonetoringCall();

            _lievViewManager = new LievViewManager();
            _lievViewManager.PrepareSchacht();
            int renderWidth = 300;
            int renderHeight = (int)Math.Round(_lievViewManager.TotalHeight);

            SharedSvgBitmap = RenderSvgToBitmap(_lievViewManager.ComposedSvg, renderWidth, renderHeight);
            SharedSvgBitmapAlternative = RenderSvgToBitmap(_lievViewManager.ComposedSvgAlternative, renderWidth, renderHeight);
            SvgImageControl.Source = SharedSvgBitmap;
            SvgImageControlAlternative.Source = SharedSvgBitmapAlternative;

            _monetoringManager = new MonetoringManager();
            _monetoringManager.Start();

            _isLogicInitialized = true;
        }

        private void ResumeLogic()
        {
            _monetoringManager?.Start();

            if (_updateTimer == null)
            {
                _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _updateTimer.Tick += (s, ev) =>
                {
                    DisplayFloor();
                    DisplayTemp();
                    DisplayLast();
                    DisplayFahrtZahler();
                    DisplayZustand();
                    DisplaySk();
                    DisplayBStunden();
                    DisplayFahrkorbMM();
                    DisplayFahrkorbAnimation();
                };
            }
            _updateTimer.Start();

            _cancellationTokenSource = new CancellationTokenSource();
            _heartbeatCancellationTokenSource = new CancellationTokenSource();
            StartPeriodicUpdateO(TimeSpan.FromSeconds(10), _cancellationTokenSource.Token);
        }

        public void PauseLogic()
        {
            _updateTimer?.Stop();
            _monetoringManager?.Stop();
            _cancellationTokenSource?.Cancel();
        }

        public void StopLogic()
        {
            _updateTimer?.Stop();
            _monetoringManager?.Stop();
            _cancellationTokenSource?.Cancel();
            _heartbeatCancellationTokenSource?.Cancel();
        }

        private static void MonetoringCall()
        {
            SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x05, 0x01, 0x01 });
            Debug.WriteLine("Send all");
        }

        private Bitmap RenderSvgToBitmap(string svgString, int width, int height)
        {
            var svg = new SKSvg();
            svg.FromSvg(svgString);

            SKPicture picture = svg.Picture;
            if (picture == null)
                throw new Exception("Das SVG konnte nicht geladen werden.");

            SKBitmap skBitmap = new SKBitmap(width, height);
            using (var canvas = new SKCanvas(skBitmap))
            {
                canvas.Clear(SKColors.Transparent);
                float scaleX = width / picture.CullRect.Width;
                float scaleY = height / picture.CullRect.Height;
                float scale = Math.Min(scaleX, scaleY);
                canvas.Scale(scale);
                canvas.DrawPicture(picture);
            }

            using (var image = SKImage.FromBitmap(skBitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = new MemoryStream())
            {
                data.SaveTo(stream);
                stream.Seek(0, SeekOrigin.Begin);
                return new Bitmap(stream);
            }
        }

        #region Anzeige-Methoden

        public void DisplayFahrkorbAnimation()
        {
            var tg1 = (TransformGroup)PositionControl.RenderTransform;
            ((TranslateTransform)tg1.Children[1]).Y = ViewModel.PositionY;
            var tg2 = (TransformGroup)PositionControl2.RenderTransform;
            ((TranslateTransform)tg2.Children[1]).Y = ViewModel.PositionY;
        }

        public void DisplayFahrkorbMM()
        {
            int mm = ViewModel.CurrentFahrkorb / Pos_Cal;
            korbPosition.Text = mm + " mm";
        }

        public void DisplayTemp()
        {
            Temp.Text = ViewModel.CurrentTemp + "°C";
        }

        public void DisplayFloor()
        {
            Etage.Text = ViewModel.CurrentFloor.ToString();
          //  EtageProgressBar.Value = ViewModel.CurrentFloor + 1;
        }

        public void DisplayFahrtZahler()
        {
            FahrtZahler.Text = ViewModel.CurrentFahrtZahler.ToString();
            Debug.WriteLine("Fahrten: " + FahrtZahler.Text);
        }

        public void DisplayBStunden()
        {
            
                int h = ViewModel.CurrentBStunden / 3600;
                int m = ViewModel.CurrentBStunden / 60;
                BStunden.Text = h + "h " + m +"min";
           
                
                
            
        }

        public void DisplayZustand()
        {
            switch (ViewModel.CurrentZustand)
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
        }

        public void DisplayLast()
        {
            Last.Text = ViewModel.CurrentLast.ToString() + "kg";
        }

        public void DisplaySk()
        {
            SK1.Background = ViewModel.CurrentSK1 == 0 ? new SolidColorBrush(Colors.Red) : new SolidColorBrush(Colors.GreenYellow);
            SK2.Background = ViewModel.CurrentSK2 == 0 ? new SolidColorBrush(Colors.Red) : new SolidColorBrush(Colors.GreenYellow);
            SK3.Background = ViewModel.CurrentSK3 == 0 ? new SolidColorBrush(Colors.Red) : new SolidColorBrush(Colors.GreenYellow);
            SK4.Background = ViewModel.CurrentSK4 == 0 ? new SolidColorBrush(Colors.Red) : new SolidColorBrush(Colors.GreenYellow);
        }

        #endregion

        #region Timer-Methoden

        private void StartFloorTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (s, e) => DisplayFloor();
            _floorTimer.Start();
        }

        private void StartFahrkorbAnimationTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (s, e) => DisplayFahrkorbAnimation();
            _floorTimer.Start();
        }

        private void StartFahrtTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (s, e) => DisplayFahrtZahler();
            _floorTimer.Start();
        }

        private void StartBStundenTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (s, e) => DisplayBStunden();
            _floorTimer.Start();
        }

        private void StartTempTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (s, e) => DisplayTemp();
            _floorTimer.Start();
        }

        private void StartLastTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (s, e) => DisplayLast();
            _floorTimer.Start();
        }

        private void StartZustandTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (s, e) => DisplayZustand();
            _floorTimer.Start();
        }

        private void StartSkTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (s, e) => DisplaySk();
            _floorTimer.Start();
        }

        private void StartKorbTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (s, e) => DisplayFahrkorbMM();
            _floorTimer.Start();
        }

        private void SetupBlinkTimer()
        {
            _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _blinkTimer.Tick += ToggleColor;
            _blinkTimer.Start();
        }

        private void ToggleColor(object sender, EventArgs e)
        {
            Temp.Foreground = _isGreen ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Colors.GreenYellow);
            _isGreen = !_isGreen;
        }

        private async void StartPeriodicUpdateO(TimeSpan interval, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try { SerialPortManager.Instance.Open(); } catch (Exception ex) { Debug.WriteLine(ex); }
                try { await Task.Delay(interval, token); } catch { Debug.WriteLine("Polling abgebrochen"); }
            }
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            _floorTimer?.Stop();
            _cancellationTokenSource?.Cancel();
            _heartbeatCancellationTokenSource?.Cancel();
            base.OnClosed(e);
        }

        #region HSE-Verbindung und Status

        public void HseConnect()
        {
            MonetoringManager.startMonetoring();
            Debug.WriteLine("HSE-Verbindung wird hergestellt...");
            SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x05, 0x01, 0x01 });
            Debug.WriteLine("Monetoring gestartet.");
            //EtageProgressBar.Maximum = gesamteFloors - 1;

            ViewModel.CurrentZustand = HseCom.SendHse(1005);
            ViewModel.CurrentStateTueur1 = HseCom.SendHse(1006);
            ViewModel.CurrentStateTueur2 = HseCom.SendHse(1016);
            ViewModel.CurrentFahrtZahler = HseCom.SendHse(2145);

            ViewModel.CurrentTemp = HseCom.SendHse(3001);

            byte[] last = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x64, 0x80 });
            try { ViewModel.CurrentLast = BitConverter.ToInt16(new byte[] { last[8], last[9] }, 0); } catch { }

            byte[] SK = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x21, 0x02, 0x00, 0x05 });
            if (SK != null && SK.Length > 10)
            {
                byte sk = SK[10];
                bool[] skArray = new bool[4];
                for (int i = 0; i < 4; i++) skArray[i] = (sk & (1 << i)) != 0;
                ViewModel.CurrentSK1 = skArray[0] ? 1 : 0;
                ViewModel.CurrentSK2 = skArray[1] ? 1 : 0;
                ViewModel.CurrentSK3 = skArray[2] ? 1 : 0;
                ViewModel.CurrentSK4 = skArray[3] ? 1 : 0;
            }

            ViewModel.CurrentFloor = HseCom.SendHse(1002);
            _monetoringManager = new MonetoringManager();
            _monetoringManager.Start();

            float lastKorbPosition = MonetoringManager.LastKorbPosition;
            int bStunden = MonetoringManager.Betriebsstunden;
            ViewModel.PositionY = float.IsNaN(lastKorbPosition) ? 0 : lastKorbPosition;
            ViewModel.CurrentBStunden = bStunden;
        }

        #endregion

        private void Button_Click_Settings(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                string tag = button.Tag?.ToString();
                if (tag == "Menu")
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
                       // SettingsText5.IsVisible = true;
                       // ButtonSettings5.Width = 100;
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
                        //SettingsText5.IsVisible = false;
                        //ButtonSettings5.Width = 50;
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
                    switch (tag)
                    {
                        case "Testrufe":
                            _cachedTestrufeWindow.Show();
                            _cachedTestrufeWindow.Activate();
                            StopLogic();
                            break;
                        case "Codes":
                            new Code().Show();
                            break;
                        case "Ansicht":
                            TerminalManager.terminalActive = true;
                            new Terminal().Show();
                            break;
                    }
                }
            }
        }

        private void SwitchTime(object? sender, RoutedEventArgs e)
        {
            BZeitSchalter = !BZeitSchalter;
            DisplayBStunden();
        }
    }
}
