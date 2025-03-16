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

namespace HSED_2._0
{
    public partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }
        public MainViewModel ViewModel { get; }
        private LievViewManager _lievViewManager;
        private MonetoringManager _monetoringManager;
        private CancellationTokenSource _cancellationTokenSource;
        private CancellationTokenSource _heartbeatCancellationTokenSource;
        private DispatcherTimer _blinkTimer;
        private DispatcherTimer _floorTimer;
        bool NavBarStatus = false;
        private bool _isGreen = false;
        public static bool BZeitSchalter = false;

        // Beispiel: Floor-Anzahl (möglicherweise dynamisch über HseCom.SendHse(1001) ermittelt)
        public int gesamteFloors = HseCom.SendHse(1001);

        public MainWindow()
        {
            InitializeComponent();
            this.Position = new PixelPoint(100, 100);
            Instance = this;
            ViewModel = new MainViewModel();
            DataContext = ViewModel;

            // HSE-Verbindung initialisieren
            HseConnect();
            MonetoringCall();

            // LievViewManager initialisieren und Schacht vorbereiten
            _lievViewManager = new LievViewManager();
            _lievViewManager.PrepareSchacht();

            // Gesamt-SVG erzeugen (kombiniert: hinterer Schacht, Fahrkorb, vorderer Schacht)


            // Rendern des SVG in ein Bitmap (Größe ggf. anpassen)
            Bitmap renderedBitmap = RenderSvgToBitmap(_lievViewManager.ComposedSvg, 300, 600);
            SvgImageControl.Source = renderedBitmap;

            // Starte weitere Timer zur Aktualisierung der Anzeige
            StartFloorTimer();
            StartTempTimer();
            StartLastTimer();
            StartFahrtTimer();
            StartZustandTimer();
            StartSkTimer();
            StartBStundenTimer();

            _cancellationTokenSource = new CancellationTokenSource();
            StartPeriodicUpdateO(TimeSpan.FromSeconds(10), _cancellationTokenSource.Token);
        }


        public static void MonetoringCall()
        {
            SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x05, 0x01, 0x01 });
            Debug.WriteLine("Send all");
        }


        /// <summary>
        /// Rendert einen SVG-String in ein Avalonia-Bitmap mithilfe von Svg.Skia und SkiaSharp.
        /// </summary>
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

        public void DisplayTemp()
        {
            Temp.Text = ViewModel.CurrentTemp.ToString() + "°C";
        }

        public void DisplayFloor()
        {
            Etage.Text = ViewModel.CurrentFloor.ToString();
            EtageProgressBar.Value = ViewModel.CurrentFloor + 1;
        }

        public void DisplayFahrtZahler()
        {
            Debug.WriteLine("Fahrten: " + FahrtZahler);
            FahrtZahler.Text = ViewModel.CurrentFahrtZahler.ToString();
        }

        public void DisplayBStunden() 
        {
            if (BZeitSchalter)
            {
                int zeit = ViewModel.CurrentBStunden / 3600;
                BStunden.Text = zeit.ToString() + " h";
            }
            else
            {
                int zeit = ViewModel.CurrentBStunden / 60;
                BStunden.Text = zeit.ToString() + " min";
            }
               

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
            Last.Text = ViewModel.CurrentLast.ToString();
        }

        public void DisplaySk()
        {
            Debug.WriteLine("SK1: " + ViewModel.CurrentSK1);
            Debug.WriteLine("SK2: " + ViewModel.CurrentSK2);
            Debug.WriteLine("SK3: " + ViewModel.CurrentSK3);
            Debug.WriteLine("SK4: " + ViewModel.CurrentSK4);
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
            _floorTimer.Tick += (sender, e) => DisplayFloor();
            _floorTimer.Start();
        }

        private void StartFahrtTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (sender, e) => DisplayFahrtZahler();
            _floorTimer.Start();
        }

        private void StartBStundenTimer() 
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (sender, e) => DisplayBStunden();
            _floorTimer.Start();


        }
        private void StartTempTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (sender, e) => DisplayTemp();
            _floorTimer.Start();
        }

        private void StartLastTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (sender, e) => DisplayLast();
            _floorTimer.Start();
        }

        private void StartZustandTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (sender, e) => DisplayZustand();
            _floorTimer.Start();
        }

        private void StartSkTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (sender, e) => DisplaySk();
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
                try
                {
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
            EtageProgressBar.Maximum = gesamteFloors - 1;

            ViewModel.CurrentZustand = HseCom.SendHse(1005);

            int temp = HseCom.SendHse(3001);
            ViewModel.CurrentTemp = temp;
            byte[] last = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x64, 0x80 });
            try
            {
                int Last = BitConverter.ToInt16(new byte[] { last[8], last[9] }, 0);
                ViewModel.CurrentLast = Last;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Fehler beim Lesen des letzten Fehlers: " + ex.Message);
            }
            
            byte[] SK = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x21, 0x02, 0x00, 0x05 });
            if (SK == null || SK.Length <= 10)
            {
                Debug.WriteLine("Ungültige Antwort für Temperatur.");
                return;
            }
            byte sk = SK[10];
            bool[] skArray = new bool[4];
            for (int i = 0; i < 4; i++)
            {
                skArray[i] = (sk & (1 << i)) != 0;
                Debug.WriteLine("Main SK" + i + ": " + skArray[i]);
            }
            ViewModel.CurrentSK1 = skArray[0] ? 1 : 0;
            ViewModel.CurrentSK2 = skArray[1] ? 1 : 0;
            ViewModel.CurrentSK3 = skArray[2] ? 1 : 0;
            ViewModel.CurrentSK4 = skArray[3] ? 1 : 0;

            int currentFloor = HseCom.SendHse(1002);
            ViewModel.CurrentFloor = currentFloor;
            Debug.WriteLine("Initialer Etagenwert: " + currentFloor);

            _monetoringManager = new MonetoringManager();
            _monetoringManager.Start();
          /*  DisplayFloor();
            DisplaySk();
            DisplayTemp();
            DisplayLast();
            DisplayZustand();
            DisplayFahrtZahler();
            DisplayBStunden();
            SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x05, 0x01, 0x01 });
            Debug.WriteLine("Send all");*/
        }

        #endregion

        // Button-Click-Handler (Settings, Navigation etc.)
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

        private void SwitchTime(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            bool state = BZeitSchalter;
            if (state) {

                BZeitSchalter = false;
                DisplayBStunden();

            }
            else
            {

                BZeitSchalter = true;
                DisplayBStunden();

            }
        }
    }
}
