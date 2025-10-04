using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Layout;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using SkiaSharp;
using Svg.Skia;
using System.IO;
using System.Text;
using HSED_2_0;
using HSED_2_0.ViewModels;
using Avalonia.Media.Imaging;

namespace HSED_2._0
{
    public partial class TestrufeNeu : Window
    {
        private CancellationTokenSource _cancellationTokenSource;
        private DispatcherTimer _blinkTimer;
        private bool NavBarStatus = false;
        private bool _isGreen = false;
        private int ZielEtage;
        private bool isAussenRuf;
        private bool AussenRufisUp;


        public static TestrufeNeu Instance { get; private set; }
        public MainViewModel ViewModel { get; }
        private LievViewManager _lievViewManager;
        private MonetoringManager _monetoringManager;
        private CancellationTokenSource _heartbeatCancellationTokenSource;
        private DispatcherTimer _floorTimer;
        private MainWindow _mainWindow;
        public int Pos_Cal = HseCom.SendHse(10101010);

        // Beispiel: Floor-Anzahl (möglicherweise dynamisch über HseCom.SendHse(1001) ermittelt)
        public int gesamteFloors = HseCom.SendHse(1001);
        private DispatcherTimer _displayTimer;
        private CancellationTokenSource? _testrufeCancellationTokenSource;
        private MainWindow _cachedMainWindow;




        public TestrufeNeu()
        {
            InitializeComponent();
            Instance = this;

            // 1) Sofort oben links positionieren
            this.Position = new PixelPoint(0, 0);

            ViewModel = MainWindow.MainViewModelInstance;
            DataContext = ViewModel;

            SvgImageControl.Source = MainWindow.SharedSvgBitmap;
            SvgImageControlAlternative.Source = MainWindow.SharedSvgBitmapAlternative;
        

            this.Opened += TestrufeNeu_Opened;
        }



        private void TestrufeNeu_Opened(object sender, EventArgs e)
        {
            this.Opened -= TestrufeNeu_Opened;
            StartLogic();
        }


        private void StartDisplayTimer()
        {
            _displayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _displayTimer.Tick += (s, ev) =>
            {
                // Beispiel: Falls keine Bindings existieren, Textblöcke manuell füllen
                Etage.Text = ViewModel.CurrentFloor.ToString();
                // Weitere manuelle Updates, falls nötig…
            };
            _displayTimer.Start();
        }

        private async void StartTestrufePolling(TimeSpan interval, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Beispiel: SK-Status abfragen
                    int GanzeSK = HseCom.SendHse(1003);
                    int[] SK = HseCom.IntToArray(GanzeSK);
                    ViewModel.CurrentSK1 = SK[0];
                    ViewModel.CurrentSK2 = SK[1];
                    ViewModel.CurrentSK3 = SK[2];
                    ViewModel.CurrentSK4 = SK[3];

                    // Floor abfragen
                    int currentFloor = HseCom.SendHse(1002);
                    ViewModel.CurrentFloor = currentFloor;

                    // Zustand abfragen
                    int AZustand = HseCom.SendHse(1005);
                    ViewModel.CurrentZustand = AZustand;

                    // Türen abfragen
                    ViewModel.CurrentStateTueur1 = HseCom.SendHse(1006);
                    ViewModel.CurrentStateTueur2 = HseCom.SendHse(1016);

                    // … beliebige weitere HSE-Abfragen für Testrufe …
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TestrufePolling-Fehler: {ex}");
                }

                try
                {
                    await Task.Delay(interval, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }


        public void StartLogic()
        {
            // 1) HSE-Verbindung initialisieren
            MainWindow.Instance.HseConnect();
            InitializeProgressbar();
            MonetoringCall(); // falls nötig

            // 2) MonitoringManager für Testrufe neu starten
            _monetoringManager = new MonetoringManager();
            _monetoringManager.Start();

            // 3) Periodisches HSE-Polling starten (eigenes CancellationTokenSource)
            _testrufeCancellationTokenSource = new CancellationTokenSource();
            StartTestrufePolling(TimeSpan.FromSeconds(5), _testrufeCancellationTokenSource.Token);

            SvgImageControl.Source = MainWindow.SharedSvgBitmap;
            SvgImageControlAlternative.Source = MainWindow.SharedSvgBitmapAlternative;

            // 4) EINEN DispatcherTimer für alle Display-Methoden
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _updateTimer.Tick += (s, ev) =>
            {
                // HIER NICHT mehr StartFloorTimer(), StartSKTimer() etc. aufrufen,
                // sondern die reinen Display-Methoden in einem Rutsch:
                StartFloorTimer();
                StartSKTimer();
                StartZustandTimer();
                StartTuerenTimer();
                GenerateFloorButtons();
                StartFahrkorbAnimationTimer();
                StartKorbTimer();
            };
            _updateTimer.Start();

            // 5) Optional: EINEN zweiten Timer, um manuelle Textfelder (z.B. Etage.Text) aus dem ViewModel zu füllen
            _displayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _displayTimer.Tick += (s, ev) =>
            {
                Etage.Text = ViewModel.CurrentFloor.ToString();
                // Falls weitere Felder per Code aktualisiert werden müssen, hier ergänzen …
            };
            _displayTimer.Start();
        }



        private DispatcherTimer _updateTimer;
        private void StartUpdateTimer()
        {
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _updateTimer.Tick += (s, e) =>
            {
                StartFloorTimer();
                StartSKTimer();
                StartZustandTimer();
                StartTuerenTimer();
                GenerateFloorButtons();
                StartFahrkorbAnimationTimer();
                StartKorbTimer();
                // … alles in einem Rutsch
            };
            _updateTimer.Start();
        }


        public void StopLogic()
        {
            // 1) Den einen DispatcherTimer für Anzeige-Updates stoppen
            _updateTimer?.Stop();

            // 2) Den optionalen Display-Timer stoppen
            _displayTimer?.Stop();

            // 3) MonitoringManager beenden
            _monetoringManager?.Stop();

            // 4) Periodisches HSE-Polling abbrechen
            _testrufeCancellationTokenSource?.Cancel();
        }





        protected override void OnClosed(EventArgs e)
        {
            // Testrufe pausieren – falls der Nutzer per „X“ schließt
            StopLogic();
            base.OnClosed(e);
        }



        private void InitializeProgressbar()
        {
            EtageProgressBar.Maximum = gesamteFloors - 1;
        }



        public void DisplayFahrkorbAnimation()
        {
            var transformGroup = (TransformGroup)PositionControl.RenderTransform;
            var YTransform = (TranslateTransform)transformGroup.Children[1];
            YTransform.Y = ViewModel.PositionY;
            transformGroup = (TransformGroup)PositionControl2.RenderTransform;
            YTransform = (TranslateTransform)transformGroup.Children[1];
            YTransform.Y = ViewModel.PositionY;

        }
        public void DisplayFloor()
        {
            Etage.Text = ViewModel.CurrentFloor.ToString();
            EtageProgressBar.Value = ViewModel.CurrentFloor + 1;
        }

        private void StartFloorTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (sender, e) => DisplayFloor();
            _floorTimer.Start();
        }

        private void StartFahrkorbAnimationTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (sender, e) => DisplayFahrkorbAnimation();
            _floorTimer.Start();
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

        public void DisplayFahrkorbMM()
        {
            int fahrkorb = ViewModel.CurrentFahrkorb;
            fahrkorb = fahrkorb / Pos_Cal;
            korbPosition.Text = fahrkorb.ToString() + " mm";
        }
        private void StartKorbTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (sender, e) => DisplayFahrkorbMM();
            _floorTimer.Start();
        }

        private void StartZustandTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (sender, e) => DisplayZustand();
            _floorTimer.Start();
        }

        private void DisplayTueren()
        {
            switch (ViewModel.CurrentStateTueur1)
            {
                case 0:
                    Tuer1.Text = "Geschlossen";
                    Tuer1.Foreground = new SolidColorBrush(Colors.White);
                    break;
                case 80:
                    Tuer1.Text = "Tür öffnet";
                    Tuer1.Foreground = new SolidColorBrush(Colors.Yellow);
                    break;
                case 48:
                    Tuer1.Text = "Tür geöffnet";
                    Tuer1.Foreground = new SolidColorBrush(Colors.GreenYellow);
                    break;
                case 32:
                    Tuer1.Text = "Tür schließt";
                    Tuer1.Foreground = new SolidColorBrush(Colors.Yellow);
                    break;
                case 96:
                    Tuer1.Text = "LS unterbrochen";
                    Tuer1.Foreground = new SolidColorBrush(Colors.Orange);
                    break;
                case 97:
                    Tuer1.Text = "Tür geöffnet";
                    Tuer1.Foreground = new SolidColorBrush(Colors.GreenYellow);
                    break;
                case 224:
                    Tuer1.Text = "Türfehler";
                    Tuer1.Foreground = new SolidColorBrush(Colors.Red);
                    break;
                case 112:
                    Tuer1.Text = "Tür gestoppt";
                    Tuer1.Foreground = new SolidColorBrush(Colors.Red);
                    break;
            }
            switch (ViewModel.CurrentStateTueur2)
            {
                case 0:
                    Tuer2.Text = "Geschlossen";
                    Tuer2.Foreground = new SolidColorBrush(Colors.White);
                    break;
                case 80:
                    Tuer2.Text = "Tür öffnet";
                    Tuer2.Foreground = new SolidColorBrush(Colors.Yellow);
                    break;
                case 48:
                    Tuer2.Text = "Tür geöffnet";
                    Tuer2.Foreground = new SolidColorBrush(Colors.GreenYellow);
                    break;
                case 32:
                    Tuer2.Text = "Tür schließt";
                    Tuer2.Foreground = new SolidColorBrush(Colors.Yellow);
                    break;
                case 96:
                    Tuer2.Text = "LS unterbrochen";
                    Tuer2.Foreground = new SolidColorBrush(Colors.Orange);
                    break;
                case 97:
                    Tuer2.Text = "Tür geöffnet";
                    Tuer2.Foreground = new SolidColorBrush(Colors.GreenYellow);
                    break;
            }
        }

        private void StartTuerenTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (sender, e) => DisplayTueren();
            _floorTimer.Start();
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

        private void StartSKTimer()
        {
            _floorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _floorTimer.Tick += (sender, e) => DisplaySk();
            _floorTimer.Start();
        }

        public static void MonetoringCall()
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

        private async void StartPeriodicUpdateO(TimeSpan interval, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    HseUpdatedO(); // Update-Methode aufrufen
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
                    HseUpdated(); // Zusätzliche Update-Logik (falls benötigt)
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
                    //SK(); // SK-Status updaten
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

        private void GenerateFloorButtons()
        {
            // Beispiel: min und max aus MonetoringManager (z. B. BootFloor und TopFloor)
            int maxFloor = MonetoringManager.TopFloor - 1;
            int minFloor = MonetoringManager.BootFloor;

            // Vorhandene Buttons im Panel löschen
            FloorButtonsPanel.Children.Clear();

            // Für jede Etage wird eine horizontale Zeile erzeugt, die drei Buttons enthält:
            // einen "Pfeil oben", einen "Pfeil unten" und einen Button mit dem Etagenwert
            for (int floor = maxFloor; floor >= minFloor; floor--)
            {
                var rowPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 5,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                // Pfeil oben
                // Pfeil oben
                var btnUp = new Button
                {
                    Width = 40,
                    Height = 40,
                    Content = "▲",
                    Tag = floor.ToString(), // Floor wird hier im Tag gespeichert
                    Background = Brushes.LightGray,
                    Foreground = Brushes.Black,
                    CornerRadius = new CornerRadius(90),
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                btnUp.Click += (sender, e) =>
                {
                    if (sender is Button button)
                    {
                        string floorStr = btnUp.Tag.ToString();
                        int ziel = Convert.ToInt32(floorStr);
                        int calculatedEtage = (1 + ziel) - MonetoringManager.BootFloor;
                        byte floor = (byte)calculatedEtage;
                        SerialPortManager.Instance.SendWithoutResponse(new byte[]
                        {   0x04, 0x01, 0x02, 0x01, 0x01, floor, 0x01, 0x01
                        });
                        Debug.WriteLine("Etagenbutton geklickt: " + floorStr);
                    }
                };


                // Pfeil unten
                var btnDown = new Button
                {
                    Width = 40,
                    Height = 40,
                    Content = "▼",
                    Tag = floor.ToString(), // Floor wird hier im Tag gespeichert
                    Background = Brushes.LightGray,
                    Foreground = Brushes.Black,
                    CornerRadius = new CornerRadius(90),
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                btnDown.Click += (sender, e) =>
                {
                    if (sender is Button button)
                    {
                        string floorStr = btnDown.Tag.ToString();
                        int ziel = Convert.ToInt32(floorStr);
                        int calculatedEtage = (1 + ziel) - MonetoringManager.BootFloor;
                        byte floor = (byte)calculatedEtage;
                        SerialPortManager.Instance.SendWithoutResponse(new byte[]
                        {
                   0x04, 0x01, 0x02, 0x02, 0x01, floor, 0x01, 0x01   });
                        Debug.WriteLine("Etagenbutton geklickt: " + floorStr);
                    }
                };


                // Etagen-Button (mittlerer Button)
                var floorBtn = new Button
                {
                    Width = 40,
                    Height = 40,
                    Content = floor.ToString(),
                    Background = Brushes.LightGray,
                    Foreground = Brushes.Black,
                    CornerRadius = new CornerRadius(25),
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                floorBtn.Click += Button_Click_Number_New;

                // Buttons zum Zeilen-Panel hinzufügen (Reihenfolge: Pfeil oben, Pfeil unten, Etagen-Button)
                rowPanel.Children.Add(btnUp);
                rowPanel.Children.Add(btnDown);
                rowPanel.Children.Add(floorBtn);

                // Füge die gesamte Zeile dem übergeordneten Panel hinzu
                FloorButtonsPanel.Children.Add(rowPanel);
            }
        }

        private void ArrowDown_Click(int floor)
        {
            Debug.WriteLine($"Pfeil runter für Etage {floor} geklickt.");
            // Hier kannst du die Logik für den Pfeil unten ergänzen
        }

        private void Button_Click_Number_New(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                string floorStr = btn.Content.ToString();
                int ziel = Convert.ToInt32(floorStr);
                int calculatedEtage = (1 + ziel) - MonetoringManager.BootFloor;
                SerialPortManager.Instance.SendWithoutResponse(new byte[]
                {
                    0x04, 0x01, 0x05, (byte)calculatedEtage, 0x01, 0x00, 0x01, 0x01
                });
                Debug.WriteLine("Etagenbutton geklickt: " + floorStr);
            }
        }

        

       /* private void HseConnect()
        {
            try
            {
                byte[] bottomfloor = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
                int topFloor = bottomfloor[10];
                byte[] bottomfloorName = new byte[2];
                bottomfloorName[0] = bottomfloor[11];
                bottomfloorName[1] = bottomfloor[10];
                string asciiString = Encoding.ASCII.GetString(bottomfloorName);
                int bootFloor = Convert.ToInt32(asciiString);
                int AllFloorsTop = HseCom.SendHse(1001);
                int obersteEtagenBezeichnung = (AllFloorsTop + bootFloor) - 1;

                var obersteTextBlock = this.FindControl<TextBlock>("EtagenInsgesamtOberste");
                var untersteTextBlock = this.FindControl<TextBlock>("EtagenInsgesamtUnterste");
                if (obersteTextBlock != null)
                    obersteTextBlock.Text = obersteEtagenBezeichnung.ToString();
                if (untersteTextBlock != null)
                    untersteTextBlock.Text = bootFloor.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            int allFloors = HseCom.SendHse(1001);
            var progressBar = this.FindControl<ProgressBar>("EtageProgressBar");
            if (progressBar != null)
                progressBar.Maximum = allFloors - 1;
            var etageTextBlock = this.FindControl<TextBlock>("Etage");
            if (etageTextBlock != null)
                etageTextBlock.Text = HseCom.SendHse(1002).ToString();
        }

        private void SK()
        {
            var zustand = this.FindControl<TextBlock>("Zustand");
            var tuer1 = this.FindControl<TextBlock>("Tuer1");
            var tuer2 = this.FindControl<TextBlock>("Tuer2");
            var sk1 = this.FindControl<Border>("SK1");
            var sk2 = this.FindControl<Border>("SK2");
            var sk3 = this.FindControl<Border>("SK3");
            var sk4 = this.FindControl<Border>("SK4");

            if (zustand != null)
                zustand.Foreground = new SolidColorBrush(Colors.Gray);
            if (tuer1 != null)
                tuer1.Foreground = new SolidColorBrush(Colors.Gray);
            if (tuer2 != null)
                tuer2.Foreground = new SolidColorBrush(Colors.Gray);
            if (sk1 != null)
                sk1.Background = new SolidColorBrush(Colors.Gray);
            if (sk2 != null)
                sk2.Background = new SolidColorBrush(Colors.Gray);
            if (sk3 != null)
                sk3.Background = new SolidColorBrush(Colors.Gray);
            if (sk4 != null)
                sk4.Background = new SolidColorBrush(Colors.Gray);
        }*/

        private void HseUpdatedO()
        {
            // Aktualisiere SK-Buttons
            var sk1 = this.FindControl<Border>("SK1");
            var sk2 = this.FindControl<Border>("SK2");
            var sk3 = this.FindControl<Border>("SK3");
            var sk4 = this.FindControl<Border>("SK4");

            int GanzeSK = HseCom.SendHse(1003);
            int[] SK = HseCom.IntToArray(GanzeSK);
            if (sk1 != null && sk2 != null && sk3 != null && sk4 != null)
            {
                sk1.Background = new SolidColorBrush(SK[0] == 0 ? Colors.Red : Colors.GreenYellow);
                sk2.Background = new SolidColorBrush(SK[1] == 0 ? Colors.Red : Colors.GreenYellow);
                sk3.Background = new SolidColorBrush(SK[2] == 0 ? Colors.Red : Colors.GreenYellow);
                sk4.Background = new SolidColorBrush(SK[3] == 0 ? Colors.Red : Colors.GreenYellow);
            }

            int currentfloor = HseCom.SendHse(1002);
            var etageTextBlock = this.FindControl<TextBlock>("Etage");
            if (currentfloor == 505 || currentfloor == 404)
                return;
            if (etageTextBlock != null)
                etageTextBlock.Text = currentfloor.ToString();

            int AZustand = HseCom.SendHse(1005);
            if (AZustand == 505 || AZustand == 404)
                return;

            int tuerZustand1 = HseCom.SendHse(1006);
            int tuerZustand2 = HseCom.SendHse(1016);
            var tuer1Text = this.FindControl<TextBlock>("Tuer1");
            var tuer2Text = this.FindControl<TextBlock>("Tuer2");

            if (tuer1Text != null)
            {
                switch (tuerZustand1)
                {
                    case 0:
                        tuer1Text.Text = "Geschlossen";
                        tuer1Text.Foreground = new SolidColorBrush(Colors.White);
                        break;
                    case 80:
                        tuer1Text.Text = "Tür öffnet";
                        tuer1Text.Foreground = new SolidColorBrush(Colors.Yellow);
                        break;
                    case 48:
                        tuer1Text.Text = "Tür geöffnet";
                        tuer1Text.Foreground = new SolidColorBrush(Colors.GreenYellow);
                        break;
                    case 32:
                        tuer1Text.Text = "Tür schließt";
                        tuer1Text.Foreground = new SolidColorBrush(Colors.Yellow);
                        break;
                    case 96:
                        tuer1Text.Text = "LS unterbrochen";
                        tuer1Text.Foreground = new SolidColorBrush(Colors.Orange);
                        break;
                    case 97:
                        tuer1Text.Text = "Tür geöffnet";
                        tuer1Text.Foreground = new SolidColorBrush(Colors.GreenYellow);
                        break;
                    case 224:
                        tuer1Text.Text = "Türfehler";
                        tuer1Text.Foreground = new SolidColorBrush(Colors.Red);
                        break;
                    case 112:
                        tuer1Text.Text = "Tür gestoppt";
                        tuer1Text.Foreground = new SolidColorBrush(Colors.Red);
                        break;
                }
            }
            if (tuer2Text != null)
            {
                switch (tuerZustand2)
                {
                    case 0:
                        tuer2Text.Text = "Geschlossen";
                        tuer2Text.Foreground = new SolidColorBrush(Colors.White);
                        break;
                    case 50:
                        tuer2Text.Text = "Tür öffnet";
                        tuer2Text.Foreground = new SolidColorBrush(Colors.Yellow);
                        break;
                    case 48:
                        tuer2Text.Text = "Tür geöffnet";
                        tuer2Text.Foreground = new SolidColorBrush(Colors.GreenYellow);
                        break;
                    case 32:
                        tuer2Text.Text = "Tür schließt";
                        tuer2Text.Foreground = new SolidColorBrush(Colors.Yellow);
                        break;
                    case 96:
                        tuer2Text.Text = "LS unterbrochen";
                        tuer2Text.Foreground = new SolidColorBrush(Colors.Orange);
                        break;
                    case 97:
                        tuer2Text.Text = "Tür geöffnet";
                        tuer2Text.Foreground = new SolidColorBrush(Colors.GreenYellow);
                        break;
                    case 224:
                        tuer2Text.Text = "Türfehler";
                        tuer2Text.Foreground = new SolidColorBrush(Colors.Red);
                        break;
                    case 112:
                        tuer2Text.Text = "Tür gestoppt";
                        tuer2Text.Foreground = new SolidColorBrush(Colors.Red);
                        break;
                }
            }

            var progressBar = this.FindControl<ProgressBar>("EtageProgressBar");
            if (progressBar != null)
                progressBar.Value = currentfloor + 1;
        }

        private void HseUpdated()
        {
            // Zusätzliche Update-Logik (falls benötigt)
        }

        // Eventhandler für Settings-Button
        private void Button_Click_Settings(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                string buttonTag = button.Tag?.ToString();
                if (buttonTag == "Menu")
                {
                    if (!NavBarStatus)
                    {
                        NavBar.Width += 100;
                        StackPanelNavBar.HorizontalAlignment = HorizontalAlignment.Left;
                        StackPanelNavBar.Margin = new Thickness(10, 25, 0, 0);
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
                        StackPanelNavBar.HorizontalAlignment = HorizontalAlignment.Center;
                        StackPanelNavBar.Margin = new Thickness(0, 25, 0, 0);
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
                          //  new Settings().Show();
                            break;
                        case "Testrufe":
                          //  var newWindowTestrufe = new TestrufeNeu();
                          //  newWindowTestrufe.Show();
                          //  this.Close();
                            break;
                        case "Codes":
                            new Code().Show();
                            break;
                        case "SelfDia":
                           // new LiveViewAnimationSimulation().Show();
                            break;
                        case "Ansicht":
                            TerminalManager.terminalActive = true;
                            new Terminal().Show();
                            break;
                        case "Home":
                            // 1) Testrufe-Logik anhalten
                            StopLogic(); // ruft nur _updateTimer.Stop(), _monetoringManager.Stop(), _testrufeCancellationTokenSource.Cancel()

                            // 2) MainWindow in den Vordergrund holen
                            var main = MainWindow.Instance;
                            main.Show();
                            main.Activate();

                            // 3) MainWindow-Logik (nur Resume) starten – kein schweres Rendering mehr
                            main.StartLogic();
                            break;
                    }
                }
            }
        }

        private void Button_Click_X(object sender, RoutedEventArgs e)
        {
            IoA.IsVisible = false;
            AOoU.IsVisible = false;
        }

        private void Button_Click_IoA(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                byte floor = (byte)ZielEtage;
                switch (button.Tag?.ToString())
                {
                    case "I":
                        isAussenRuf = false;
                        IoA.IsVisible = false;
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x04, 0x01, 0x05, floor, 0x01, 0x00, 0x01, 0x01 });
                        break;
                    case "A":
                        IoA.IsVisible = false;
                        AOoU.IsVisible = true;
                        break;
                }
            }
        }

        private void Button_Click_AOoU(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                byte floor = (byte)ZielEtage;
                switch (button.Tag?.ToString())
                {
                    case "R":
                        AOoU.IsVisible = false;
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x04, 0x01, 0x02, 0x02, 0x01, floor, 0x01, 0x01 });
                        break;
                    case "H":
                        AOoU.IsVisible = false;
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x04, 0x01, 0x02, 0x01, 0x01, floor, 0x01, 0x01 });
                        break;
                }
            }
        }

        private void Button_Click_Number(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                string buttonTag = button.Content?.ToString();
                switch (buttonTag)
                {
                    case "-2":
                        ZielEtage = 1;
                        break;
                    case "-1":
                        ZielEtage = 2;
                        break;
                    case "0":
                        ZielEtage = 3;
                        break;
                    case "1":
                        ZielEtage = 4;
                        break;
                    case "2":
                        ZielEtage = 5;
                        break;
                    case "3":
                        ZielEtage = 6;
                        break;
                    case "4":
                        ZielEtage = 7;
                        break;
                    case "5":
                        ZielEtage = 8;
                        break;
                    case "6":
                        ZielEtage = 9;
                        break;
                    case "7":
                        ZielEtage = 10;
                        break;
                    case "8":
                        ZielEtage = 11;
                        break;
                    case "9":
                        ZielEtage = 12;
                        break;
                    case "10":
                        ZielEtage = 13;
                        break;
                    case "11":
                        ZielEtage = 14;
                        break;
                    case "12":
                        ZielEtage = 15;
                        break;
                    case "13":
                        ZielEtage = 16;
                        break;
                    case "14":
                        ZielEtage = 17;
                        break;
                    case "15":
                        ZielEtage = 18;
                        break;
                    case "16":
                        ZielEtage = 19;
                        break;
                    case "17":
                        ZielEtage = 20;
                        break;
                    case "18":
                        ZielEtage = 21;
                        break;
                    case "19":
                        ZielEtage = 22;
                        break;
                    case "20":
                        ZielEtage = 23;
                        break;
                    case "21":
                        ZielEtage = 24;
                        break;
                }
                // Sobald ein Etagenwert eingegeben wurde, soll der Bereich IoA sichtbar werden.
                IoA.IsVisible = true;
            }
        }

        private void Button_Click_OMU(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                string buttonTag = button.Tag?.ToString();
                if (buttonTag == "OFahren")
                {
                    MainWindow mainWindow = new MainWindow();
                    int floors = mainWindow.gesamteFloors;
                    byte bytefloors = (byte)floors;
                    SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x04, 0x01, 0x05, bytefloors, 0x01, 0x00, 0x01, 0x01 });
                }
                if (buttonTag == "MFahren")
                {
                    int floors = HseCom.SendHse(1001);
                    floors = floors / 2;
                    byte bytefloors = (byte)floors;
                    SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x04, 0x01, 0x05, bytefloors, 0x01, 0x00, 0x01, 0x01 });
                }
                if (buttonTag == "UFahren")
                {
                    SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x04, 0x01, 0x05, 0x01, 0x01, 0x00, 0x01, 0x01 });
                }
            }
        }

        // Eventhandler für PointerPressed im XAML – stelle sicher, dass die Signatur korrekt ist
        private void Border_PointerPressed(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (sender is Border border)
            {
                string buttonTag = border.Tag?.ToString();
                if (buttonTag == "OFahren")
                {
                    MainWindow mainWindow = new MainWindow();
                    int floors = mainWindow.gesamteFloors;
                    byte bytefloors = (byte)floors;
                    HseCom.SendHseCommand(new byte[] { 0x04, 0x01, 0x05, bytefloors, 0x01, 0x00, 0x01, 0x01 });
                }
                if (buttonTag == "MFahren")
                {
                    int floors = HseCom.SendHse(1001);
                    floors = floors / 2;
                    byte bytefloors = (byte)floors;
                    HseCom.SendHseCommand(new byte[] { 0x04, 0x01, 0x05, bytefloors, 0x01, 0x00, 0x01, 0x01 });
                }
                if (buttonTag == "UFahren")
                {
                    HseCom.SendHseCommand(new byte[] { 0x04, 0x01, 0x05, 0x01, 0x01, 0x00, 0x01, 0x01 });
                }
            }
        }

        private void Border_PointerPressed_1(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            // Lösche evtl. vorhandenen Text, wenn der Eingabebereich den Standardtext enthält
            if (Input.Text == "Etagenwert" || Input.Text == "Etage exestiert nicht!" || Input.Text == "Bitte erneut probieren.")
                Input.Text = "";
            if (Input.Text == "0")
                Input.Text = "-";

            if (sender is Border border)
            {
                string buttonTag = border.Tag?.ToString();
                switch (buttonTag)
                {
                    case "0":
                        Input.Text += "0";
                        break;
                    case "1":
                        Input.Text += "1";
                        break;
                    case "2":
                        Input.Text += "2";
                        break;
                    case "3":
                        Input.Text += "3";
                        break;
                    case "4":
                        Input.Text += "4";
                        break;
                    case "5":
                        Input.Text += "5";
                        break;
                    case "6":
                        Input.Text += "6";
                        break;
                    case "7":
                        Input.Text += "7";
                        break;
                    case "8":
                        Input.Text += "8";
                        break;
                    case "9":
                        Input.Text += "9";
                        break;
                }
            }
        }

        private void Border_PointerPressed_2(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (sender is Border border)
            {
                string buttonTag = border.Tag?.ToString();
                if (Input.Text == "Etagenwert" || Input.Text == "Etage exestiert nicht!" || Input.Text == "Bitte erneut probieren.")
                    return;

                int input = Convert.ToInt32(Input.Text);
                byte byteFloors;

                byte[] bottomfloor = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
                int topFloor = bottomfloor[10];
                byte[] bottomfloorName = new byte[2];
                bottomfloorName[0] = bottomfloor[11];
                bottomfloorName[1] = bottomfloor[10];
                string asciiString = Encoding.ASCII.GetString(bottomfloorName);
                int bootFloor = Convert.ToInt32(asciiString);
                int AllFloorsTop = HseCom.SendHse(1001);
                int obersteEtagenBezeichnung = (AllFloorsTop + bootFloor) - 1;

                if (input > obersteEtagenBezeichnung)
                {
                    Input.Text = "Etage exestiert nicht!";
                }
                else
                {
                    if (buttonTag == "I")
                    {
                        int bootFloor2 = Convert.ToInt32(asciiString);
                        input = input - bootFloor2 + 1;
                        byteFloors = (byte)input;
                        HseCom.SendHseCommand(new byte[] { 0x04, 0x01, 0x05, byteFloors, 0x01, 0x00, 0x01, 0x01 });
                        Input.Text = "Etagenwert";
                    }
                    else if (buttonTag == "A")
                    {
                        Numpad.IsVisible = false;
                        Aussengruppe.IsVisible = true;
                    }
                    else
                    {
                        Input.Text = "Bitte erneut probieren.";
                    }
                }
            }
        }

        private void Border_PointerPressed_3(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (sender is Border border)
            {
                string buttonTag = border.Tag?.ToString();
                int input = Convert.ToInt32(Input.Text);
                if (buttonTag == "Hoch")
                {
                    byte[] bottomfloor2 = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
                    byte[] bottomfloorName2 = new byte[2];
                    bottomfloorName2[0] = bottomfloor2[11];
                    bottomfloorName2[1] = bottomfloor2[10];
                    string asciiString2 = Encoding.ASCII.GetString(bottomfloorName2);
                    int bootFloor2 = Convert.ToInt32(asciiString2);

                    input = input - bootFloor2 + 1;
                    byte byteFloors = (byte)input;
                    HseCom.SendHseCommand(new byte[] { 0x04, 0x01, 0x02, 0x01, 0x01, byteFloors, 0x01, 0x01 });
                    Input.Text = "Etagenwert";
                    Numpad.IsVisible = true;
                    Aussengruppe.IsVisible = false;
                }
                if (buttonTag == "Runter")
                {
                    byte[] bottomfloor2 = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
                    byte[] bottomfloorName2 = new byte[2];
                    bottomfloorName2[0] = bottomfloor2[11];
                    bottomfloorName2[1] = bottomfloor2[10];
                    string asciiString2 = Encoding.ASCII.GetString(bottomfloorName2);
                    int bootFloor2 = Convert.ToInt32(asciiString2);

                    input = input - bootFloor2 + 1;
                    byte byteFloors = (byte)input;
                    HseCom.SendHseCommand(new byte[] { 0x04, 0x01, 0x02, 0x02, 0x01, byteFloors, 0x01, 0x01 });
                    Input.Text = "Etagenwert";
                    Numpad.IsVisible = true;
                    Aussengruppe.IsVisible = false;
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            // Falls benötigt, Logik hier ergänzen.
        }
    }
}
