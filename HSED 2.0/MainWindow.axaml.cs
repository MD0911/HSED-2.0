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
using Avalonia.Controls.Platform;
using System.Collections.Generic;

namespace HSED_2._0
{
    public partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }
        public static MainViewModel MainViewModelInstance => Instance?.ViewModel;
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
        public int Pos_Cal = HseCom.SendHse(10101010);
        public int gesamteFloors = HseCom.SendHse(1001);

        // SVG Cache
        public static Bitmap SharedSvgBitmap { get; private set; }
        public static Bitmap SharedSvgBitmapAlternative { get; private set; }
        private TestrufeNeu _cachedTestrufeWindow;
        private bool _isLogicInitialized = false;
        private DispatcherTimer _updateTimer;

        // Viewport Scroll
        private const double ViewportStepSmall = 95;
        private const double ViewportStepBig = 220;
        private double _viewportOffsetY = 0;
        private double _viewportMinY = 0;
        private double _viewportMaxY = 0;

        // AutoFollow: jetzt standardmäßig True
        private bool _autoFollow = true;

        // Zustandsverfolgung für Sichtbarkeit und Bewegung
        private bool _wasCarVisible = false;
        private bool _wasMoving = false;

        // Sichtbarkeits-Hysterese
        private int _fullyVisibleTicks = 0;
        private const int FullyVisibleMinTicks = 3;      // 3 Ticks x 50ms = 150ms
        private const double VisibilityMarginPx = 8.0;   // kleiner Innenabstand, damit "wirklich" voll drin

        // NEU: zusätzliche Wartezeit nach vollständiger Sicht
        private const int PostEnableDelayMs = 1000;
        private int _postDelayTicks = 0;
        private int PostDelayTicksNeeded => (int)Math.Ceiling(PostEnableDelayMs / (double)FrameMs);



        // Fahrkorb-Animation (SmoothDamp)
        private DispatcherTimer _animTimer;
        private double _visY;      // sichtbarer Wert
        private double _velY;      // Geschwindigkeit
        private const int FrameMs = 50;         // 20 FPS
        private const double SmoothTime = 0.16; // Dämpfung
        private const double MaxSpeed = 6000.0;
        private const double SnapEps = 0.3;

        private enum AutoFollowMode { Follow, ManualLatch }

        private AutoFollowMode _autoFollowMode = AutoFollowMode.Follow; // Start aktiv
        private bool _seenOutOfViewAfterManual = false;

        private bool AutoFollow => _autoFollowMode == AutoFollowMode.Follow;



        private const double FloorPitch = 95.0;          // bleibt
private const double FloorAnchorOffsetPx = 210; // NEU: globaler Start-Offset nach unten

        // --- Styling ---
        private const double InsideSize = 28.0; // Innenruf: runder Button (Breite=Höhe)
        private const double InsideFontSize = 12.0; // Schrift für Etagenlabel (zweistellig passt noch)
        private const double ArrowSize = 28.0; // Außenruf-Pfeile
        private const double ArrowStackGap = 6.0;  // Abstand zwischen ▲ und ▼ beim Stapeln
        private const double GapToShaft = 6.0;  // Abstand Buttons zum Schacht
        private const double HorizontalOffsetPx = 17.5; // alles 15px weiter nach rechts


        private bool _overlaySized = false;
        private bool _buildingOverlay = false;

        // Map: Etagen-Label (z.B. -1, 0, 1, 2, …) -> Innenruftaster-Button
        private readonly Dictionary<int, Button> _insideButtons = new();





        public MainWindow()
        {
            InitializeComponent();
            Instance = this;

            ViewModel = new MainViewModel();
            DataContext = ViewModel;

            TestrufeService.StartBackgroundUpdate();

            this.Opened += MainWindow_Opened;

            _cachedTestrufeWindow = new TestrufeNeu();
            _cachedTestrufeWindow.Hide();

            this.Position = new PixelPoint(0, 0);
        }

        private void MainWindow_Opened(object sender, EventArgs e)
        {
            this.Opened -= MainWindow_Opened;
            StartLogic();
        }

        public void StartLogic()
        {
            if (!_isLogicInitialized)
            {
                InitializeLogic();
            }
            ResumeLogic();
        }

        private void InitializeLogic()
        {
            // 1) HSE & Monitoring
            HseConnect();
            MonetoringCall();

            // 2) LiveView vorbereiten & SVG rendern
            _lievViewManager = new LievViewManager();
            _lievViewManager.PrepareSchacht();

            int renderWidth = 300;
            int renderHeight = (int)Math.Round(_lievViewManager.TotalHeight);
            SharedSvgBitmap = RenderSvgToBitmap(_lievViewManager.ComposedSvg, renderWidth, renderHeight);
            SharedSvgBitmapAlternative = RenderSvgToBitmap(_lievViewManager.ComposedSvgAlternative, renderWidth, renderHeight);
            SvgImageControl.Source = SharedSvgBitmap;
            SvgImageControlAlternative.Source = SharedSvgBitmapAlternative;

            // 3) Monitoring
            _monetoringManager = new MonetoringManager();
            _monetoringManager.Start();

            _isLogicInitialized = true;

            // Scrollgrenzen
            SetupLiveViewScrollBounds();

            Dispatcher.UIThread.Post(() =>
            {
                SizeOverlayForShaft();            // Größe EINMAL festlegen
                BuildOrUpdateFloorButtonsOverlay(); // Buttons setzen
            }, DispatcherPriority.Loaded);

        }



        private void DisableAutoFollowForManual()
        {
            _autoFollowMode = AutoFollowMode.ManualLatch;
            _seenOutOfViewAfterManual = !IsCarVisible();

            // Entprellung zurücksetzen
            _fullyVisibleTicks = 0;
            _postDelayTicks = 0;
        }


        private void UpdateAutoFollowState()
        {
            bool visible = IsCarVisible();

            if (_autoFollowMode == AutoFollowMode.ManualLatch)
            {
                // erst muss er mindestens einmal unsichtbar gewesen sein
                if (!visible)
                {
                    _seenOutOfViewAfterManual = true;
                    _fullyVisibleTicks = 0;
                    _postDelayTicks = 0;
                    return;
                }

                // danach: nur wenn vollständig sichtbar, stabil halten
                if (_seenOutOfViewAfterManual && IsCarFullyVisible(VisibilityMarginPx))
                {
                    // Schritt 1: voll sichtbar stabilisieren
                    if (_fullyVisibleTicks < FullyVisibleMinTicks)
                    {
                        _fullyVisibleTicks++;
                        _postDelayTicks = 0; // Post Delay beginnt erst nach Erreichen der Vollsicht Stabilität
                        return;
                    }

                    // Schritt 2: zusätzliche Verzögerung, weiterhin volle Sicht erforderlich
                    _postDelayTicks++;
                    if (_postDelayTicks >= PostDelayTicksNeeded)
                    {
                        _autoFollowMode = AutoFollowMode.Follow;
                        _seenOutOfViewAfterManual = false;
                        _fullyVisibleTicks = 0;
                        _postDelayTicks = 0;
                    }
                }
                else
                {
                    // nicht vollständig sichtbar oder nur teilweise sichtbar
                    // Zähler zurücksetzen, damit erst bei durchgehend voller Sicht neu gezählt wird
                    _fullyVisibleTicks = 0;
                    _postDelayTicks = 0;
                }
            }
        }





        private void ManualScroll(double delta)
        {
            DisableAutoFollowForManual();
            ApplyViewportOffset(delta);
        }

        private bool IsCarFullyVisible(double margin = 0)
        {
            var carTopLeft = FahrkorbImage.TranslatePoint(new Avalonia.Point(0, 0), LiveViewBorder);
            if (carTopLeft is null) return false;

            double carTop = carTopLeft.Value.Y;
            double carBottom = carTop + FahrkorbImage.Bounds.Height;
            double viewportHeight = LiveViewBorder.Bounds.Height;

            // komplett innerhalb des Viewports (mit kleinem Margin)
            return carTop >= margin && carBottom <= (viewportHeight - margin);
        }

        private bool IsCarVisible(double margin = 0)
        {
            var carTopLeft = FahrkorbImage.TranslatePoint(new Avalonia.Point(0, 0), LiveViewBorder);
            if (carTopLeft is null) return false;

            double carTop = carTopLeft.Value.Y;
            double carBottom = carTop + FahrkorbImage.Bounds.Height;
            double viewportHeight = LiveViewBorder.Bounds.Height;

            // irgendein Teil sichtbar (mit Margin)
            return carBottom > margin && carTop < (viewportHeight - margin);
        }





        private void SizeOverlayForShaft()
        {
            if (SharedSvgBitmap == null || _overlaySized) return;

            // Schacht-Rahmen holen
            var (left, right, top, bottom) = GetShaftFrameInOverlay();

            // Breite: rechts neben dem Schacht Platz für zwei Pfeil-Buttons + Abstand
            double arrowW = 28, arrowGap = 8, gapToShaft = 6;
            double needW = Math.Max(LiveViewRoot.Bounds.Width, right + gapToShaft + arrowW * 2 + arrowGap + 2);

            // Höhe: komplette Schachthöhe (unten = bottom)
            double needH = Math.Max(LiveViewRoot.Bounds.Height, bottom + FloorAnchorOffsetPx + 2);

            FloorButtonsOverlay.Width = needW;
            FloorButtonsOverlay.Height = needH;

            _overlaySized = true;
        }


        private void ResumeLogic()
        {
            _monetoringManager?.Start();

            // UI-Update-Timer
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
                    DisplayDatum();
                    DisplayUhr();
                    DisplayInnenruftasterquittung();
                    DisplayAussenruftasterquittung();
                };
            }
            _updateTimer.Start();

            // Smooth-Follow Fahrkorb
            if (_animTimer == null)
            {
                _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FrameMs) };
                _animTimer.Tick += (s, ev) =>
                {
                    double target = ViewModel.PositionY;
                    if (double.IsNaN(_visY)) _visY = target;

                    double dt = FrameMs / 1000.0;
                    _visY = SmoothDamp(_visY, target, ref _velY, SmoothTime, MaxSpeed, dt);
                    if (Math.Abs(target - _visY) <= SnapEps)
                    {
                        _visY = target;
                        _velY = 0.0;
                    }
                    SetCarTransform(-_visY);

                    UpdateAutoFollowState();      // erst State updaten
                    if (AutoFollow)               // dann ggf. nachführen
                        AutoFollowIfNeeded();
                };



            }
            _animTimer.Start();

            // Rebuild (falls Boot/Top geändert)
            BuildOrUpdateFloorButtonsOverlay();

            // Polling
            _cancellationTokenSource = new CancellationTokenSource();
            StartPeriodicUpdateO(TimeSpan.FromSeconds(10), _cancellationTokenSource.Token);
        }
        // Fahrkorb-Transform zentral setzen (Schacht bleibt fix)
        private void SetCarTransform(double y)
        {
            // Schacht-Canvases nicht verschieben
            ((TranslateTransform)((TransformGroup)PositionControl.RenderTransform).Children[1]).Y = 0;
            ((TranslateTransform)((TransformGroup)PositionControl2.RenderTransform).Children[1]).Y = 0;

            // Nur den Fahrkorb vertikal bewegen
            var carGroup = (TransformGroup)FahrkorbImage.RenderTransform;
            var carTrans = (TranslateTransform)carGroup.Children[1];
            carTrans.Y = y;
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

        protected override void OnClosed(EventArgs e)
        {
            _floorTimer?.Stop();
            _cancellationTokenSource?.Cancel();
            _heartbeatCancellationTokenSource?.Cancel();
            _animTimer?.Stop();
            base.OnClosed(e);
        }

        // ===================== Utils =====================

        private static double SmoothDamp(double current, double target, ref double currentVelocity, double smoothTime, double maxSpeed, double deltaTime)
        {
            smoothTime = Math.Max(0.0001, smoothTime);
            double omega = 2.0 / smoothTime;

            double x = omega * deltaTime;
            double exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);

            double change = current - target;
            double maxChange = maxSpeed * smoothTime;
            change = Math.Clamp(change, -maxChange, maxChange);
            double tempTarget = current - change;

            double temp = (currentVelocity + omega * change) * deltaTime;
            currentVelocity = (currentVelocity - omega * temp) * exp;

            double output = tempTarget + (change + temp) * exp;

            if ((target - current > 0.0) == (output > target))
            {
                output = target;
                currentVelocity = (output - target) / deltaTime;
            }
            return output;
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

        // ===================== Anzeige =====================

        public void DisplayFahrkorbAnimation()
        {
            ((TranslateTransform)((TransformGroup)PositionControl.RenderTransform).Children[1]).Y = 0;
            ((TranslateTransform)((TransformGroup)PositionControl2.RenderTransform).Children[1]).Y = 0;

            var carGroup = (TransformGroup)FahrkorbImage.RenderTransform;
            var carTrans = (TranslateTransform)carGroup.Children[1];

            double y = -ViewModel.PositionY;
            carTrans.Y = y;

            AutoFollowIfNeeded();
        }

        private void AutoFollowIfNeeded()
        {
            if (!_autoFollow) return;

            var carTopLeft = FahrkorbImage.TranslatePoint(new Avalonia.Point(0, 0), LiveViewBorder);
            if (carTopLeft is null) return;

            double carTop = carTopLeft.Value.Y;
            double carBottom = carTop + FahrkorbImage.Bounds.Height;
            double viewportHeight = LiveViewBorder.Bounds.Height;
            const double margin = 60;

            if (carTop < margin)
            {
                double delta = margin - carTop;
                ApplyViewportOffset(+delta);
            }
            else if (carBottom > viewportHeight - margin)
            {
                double delta = (viewportHeight - margin) - carBottom;
                ApplyViewportOffset(delta);
            }
        }

        public void DisplayFahrkorbMM()
        {
            int fahrkorb = ViewModel.CurrentFahrkorb / Pos_Cal;
            double fahrkorbMeter = fahrkorb / 1000.0;
            fahrkorbMeter = Math.Ceiling(fahrkorbMeter * 100) / 100;
            Hoehe.Text = fahrkorb.ToString() + "mm";
        }

        private Button? FindInsideButtonByLabel(int label)
        {
            foreach (var child in FloorButtonsOverlay.Children)
            {
                if (child is Button b && b.Tag is int t && t == label)
                    return b;
            }
            return null;
        }

        public void DisplayInnenruftasterquittung()
        {
            int index = ViewModel.InnenruftasterquittungEtage;     // 1 = unterste
            int zustand = ViewModel.InnenruftasterquittungZustand; // 1 aktiv, 0 inaktiv
            if (index <= 0) return;

            // Index in sichtbares Etagenlabel umrechnen
            int label = MonetoringManager.BootFloor + (index - 1);

            // passenden Innenruftaster-Button links finden
            var btn = FindInsideButtonByLabel(label);
            if (btn is null) return;

            // nur die Kontur setzen
            var green = new SolidColorBrush(Color.Parse("#22c55e"));
            var gray = new SolidColorBrush(Color.Parse("#d1d5db"));

            btn.BorderBrush = (zustand == 1) ? green : gray;
            btn.BorderThickness = (zustand == 1) ? new Thickness(3.0) : new Thickness(1.5);
            // Hintergrund bleibt wie er ist
        }

        private static readonly SolidColorBrush BorderGreen = new(Color.Parse("#22c55e"));
        private static readonly SolidColorBrush BorderGray = new(Color.Parse("#d1d5db"));

        private void SetButtonOutline(Button btn, bool active)
        {
            btn.BorderBrush = active ? BorderGreen : BorderGray;
            btn.BorderThickness = active ? new Thickness(3.0) : new Thickness(1.5);
        }

        private Button? FindArrowButtonByLabel(int label, ArrowDir dir)
        {
            foreach (var child in FloorButtonsOverlay.Children)
            {
                if (child is Button b && b.Tag is ValueTuple<int, ArrowDir> t)
                {
                    if (t.Item1 == label && t.Item2 == dir)
                        return b;
                }
            }
            return null;
        }

        public void DisplayAussenruftasterquittung()
        {
            // Aufwärts
            int upIndex = ViewModel.AufAruftasterquittungEtage;     // 1 = unterste
            int upState = ViewModel.AufAruftasterquittungZustand;   // 1 aktiv, 0 inaktiv
            if (upIndex > 0)
            {
                int upLabel = MonetoringManager.BootFloor + (upIndex - 1);
                var upBtn = FindArrowButtonByLabel(upLabel, ArrowDir.Up);
                if (upBtn != null)
                    SetButtonOutline(upBtn, upState == 1);
            }

            // Abwärts
            int downIndex = ViewModel.AbAruftasterquittungEtage;      // 1 = unterste
            int downState = ViewModel.AbAruftasterquittungZustand;    // 1 aktiv, 0 inaktiv
            if (downIndex > 0)
            {
                int downLabel = MonetoringManager.BootFloor + (downIndex - 1);
                var downBtn = FindArrowButtonByLabel(downLabel, ArrowDir.Down);
                if (downBtn != null)
                    SetButtonOutline(downBtn, downState == 1);
            }
        }





        public void DisplayDatum() => Datum.Text = DateTime.Now.ToString("dd.MM.yyyy");
        public void DisplayUhr() => Uhr.Text = DateTime.Now.ToString("HH:mm:ss");
        public void DisplayTemp() => Temp.Text = ViewModel.CurrentTemp.ToString() + "°C";
        public void DisplayFloor() => Etage.Text = ViewModel.CurrentFloor.ToString();

        public void DisplayFahrtZahler()
        {
            FahrtZahler.Text = ViewModel.CurrentFahrtZahler.ToString();
            Debug.WriteLine("Fahrten: " + FahrtZahler.Text);
        }

        public void DisplayBStunden()
        {
            int h = ViewModel.CurrentBStunden / 3600;
            int m = ViewModel.CurrentBStunden / 60 - (h * 60);
            BStunden.Text = $"{h}h {m}min";
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
                    Zustand.Foreground = new SolidColorBrush(Color.Parse("#22c55e"));
                    break;
                case 6:
                    Zustand.Text = "Einfahrt";
                    Zustand.Foreground = new SolidColorBrush(Color.Parse("#facc15"));
                    break;
                case 17:
                    Zustand.Text = "SK Fehlt";
                    Zustand.Foreground = new SolidColorBrush(Color.Parse("#ef4444"));
                    break;
            }
        }

        public void DisplayLast() => Last.Text = ViewModel.CurrentLast.ToString() + "Kg";

        public void DisplaySk()
        {
            SK1.Background = ViewModel.CurrentSK1 == 0 ? new SolidColorBrush(Color.Parse("#9ca3af")) : new SolidColorBrush(Color.Parse("#22c55e"));
            SK2.Background = ViewModel.CurrentSK2 == 0 ? new SolidColorBrush(Color.Parse("#9ca3af")) : new SolidColorBrush(Color.Parse("#22c55e"));
            SK3.Background = ViewModel.CurrentSK3 == 0 ? new SolidColorBrush(Color.Parse("#9ca3af")) : new SolidColorBrush(Color.Parse("#22c55e"));
            SK4.Background = ViewModel.CurrentSK4 == 0 ? new SolidColorBrush(Color.Parse("#9ca3af")) : new SolidColorBrush(Color.Parse("#22c55e"));
        }

        // ===================== Timer =====================

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
                try { SerialPortManager.Instance.Open(); }
                catch (Exception ex) { Debug.WriteLine(ex); }
                try { await Task.Delay(interval, token); }
                catch (Exception ex) { Debug.WriteLine(ex); }
            }
        }

        private void SetupLiveViewScrollBounds()
        {
            var contentHeightPx = SharedSvgBitmap?.PixelSize.Height ?? 0;
            double schachtScale = GetSchachtScale();
            double scaledContentHeight = contentHeightPx * schachtScale;

            double viewportHeight = LiveViewRoot.Bounds.Height;
            double overflow = scaledContentHeight - viewportHeight;

            _viewportMinY = overflow > 0 ? -overflow : 0;
            _viewportMaxY = 0;

            _viewportOffsetY = Math.Clamp(_viewportOffsetY, _viewportMinY, _viewportMaxY);
            GetViewportTransform().Y = _viewportOffsetY;
        }

        private TranslateTransform GetViewportTransform()
        {
            var tg = (TransformGroup)LiveViewRoot.RenderTransform;
            return (TranslateTransform)tg.Children[0];
        }

        private void ApplyViewportOffset(double delta)
        {
            _viewportOffsetY = Math.Clamp(_viewportOffsetY + delta, _viewportMinY, _viewportMaxY);
            GetViewportTransform().Y = _viewportOffsetY;
        }

        // Scroll Buttons
        private void ScrollUp_Click(object? s, RoutedEventArgs e) => ManualScroll(+ViewportStepSmall);
        private void ScrollDown_Click(object? s, RoutedEventArgs e) => ManualScroll(-ViewportStepSmall);

        // AutoFollow Checkbox
        private void AutoFollow_Checked(object? s, RoutedEventArgs e) => _autoFollow = true;
        private void AutoFollow_Unchecked(object? s, RoutedEventArgs e) => _autoFollow = false;

        // ===================== HSE =====================

        public static void MonetoringCall()
        {
            SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x05, 0x01, 0x01 });
            Debug.WriteLine("Send all");
        }

        public void HseConnect()
        {
            MonetoringManager.startMonetoring();
            Debug.WriteLine("HSE-Verbindung wird hergestellt...");
            SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x05, 0x01, 0x01 });
            Debug.WriteLine("Monetoring gestartet.");

            ViewModel.CurrentZustand = HseCom.SendHse(1005);
            ViewModel.CurrentStateTueur1 = HseCom.SendHse(1006);
            ViewModel.CurrentStateTueur2 = HseCom.SendHse(1016);
            ViewModel.CurrentFahrtZahler = HseCom.SendHse(2145);

            var transformGroup = (TransformGroup)PositionControl.RenderTransform;
            var YTransform = (TranslateTransform)transformGroup.Children[1];

            int temp = HseCom.SendHse(3001);
            ViewModel.CurrentTemp = temp;

            byte[] last = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x64, 0x80 });
            try
            {
                int Last = BitConverter.ToInt16(new byte[] { last[8], last[9] }, 0);
                ViewModel.CurrentLast = Last;
            }
            catch (Exception ex) { Debug.WriteLine("Fehler beim Lesen des letzten Fehlers: " + ex.Message); }

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

            int currentFloor = HseCom.SendHse(1002);
            ViewModel.CurrentFloor = currentFloor;

            _monetoringManager = new MonetoringManager();
            _monetoringManager.Start();

            float lastKorbPosition = MonetoringManager.LastKorbPosition;
            int bStunden = MonetoringManager.Betriebsstunden;

            if (lastKorbPosition == null)
            {
                ViewModel.PositionY = 0;
            }
            else
            {
                ViewModel.PositionY = lastKorbPosition;
                ViewModel.CurrentBStunden = bStunden;
            }

            _visY = ViewModel.PositionY;
            _velY = 0.0;
        }

        // ===================== Floor Buttons Overlay =====================

        private enum ArrowDir { Up, Down }

        private bool _floorButtonsGenerated = false;
        private int _lastBootFloor = int.MinValue;
        private int _lastTopLabel = int.MinValue;
        private double _lastOverlayW = -1;
        private double _lastOverlayH = -1;

        private double GetSchachtScale()
        {
            try
            {
                var tg = (TransformGroup)PositionControl.RenderTransform;
                return ((ScaleTransform)tg.Children[0]).ScaleY; // ScaleX==ScaleY
            }
            catch { return 0.5; }
        }

        private (double left, double right, double top, double bottom) GetShaftFrameInOverlay()
        {
            var p1 = PositionControl.TranslatePoint(new Avalonia.Point(0, 0), FloorButtonsOverlay) ?? new Avalonia.Point(0, 0);
            var p2 = PositionControl2.TranslatePoint(new Avalonia.Point(0, 0), FloorButtonsOverlay) ?? new Avalonia.Point(0, 0);

            double scale = GetSchachtScale();
            double visualWidth = PositionControl.Bounds.Width * scale;                     // z.B. 300 * 0.5 = 150
            double visualHeight = (SharedSvgBitmap?.PixelSize.Height ?? 0) * scale;         // komplette Schacht-Höhe

            double top = Math.Min(p1.Y, p2.Y);
            double left = Math.Min(p1.X, p2.X);
            double right = left + visualWidth;
            double bottom = top + visualHeight;

            return (left, right, top, bottom);
        }



        private void BuildOrUpdateFloorButtonsOverlay()
        {
            if (_buildingOverlay) 
            {
                _insideButtons.Clear();
                return; // Reentrancy-Schutz
            }
            
            _buildingOverlay = true;
            try
            {
                if (SharedSvgBitmap == null) return;

                int bootLabel = MonetoringManager.BootFloor; // unterste Etage (z.B. -1)
                int count = HseCom.SendHse(1001);         // Anzahl Etagen
                if (count <= 0) return;
                int topLabel = bootLabel + count - 1;

                // Schacht-Rahmen (Overlay-Koordinaten)
                var (shaftL, shaftR, shaftTop, shaftBot) = GetShaftFrameInOverlay();

                // Layout/Größen — nur für X-Positionen, Overlay-Größe wird HIER NICHT geändert!
                double gapToShaft = 6;
                double insideW = 40, insideH = 28;                 // Innenruf links
                double arrowW = 28, arrowH = 28, arrowGap = 8;   // Pfeile rechts

                double xLeftInside = Math.Max(2, shaftL - gapToShaft - insideW);
                double xRightPair = shaftR + gapToShaft;         // ▲ ▼ nebeneinander
                double xRightSingle = shaftR + gapToShaft;         // nur ▲ oder ▼

                // Neu zeichnen (keine inkrementellen Adds, um Versatz zu vermeiden)
                FloorButtonsOverlay.Children.Clear();

                for (int f = bootLabel; f <= topLabel; f++)
                {
                    int idxFromBottom = f - bootLabel; // 0..count-1

                    double yCenter = shaftBot - (idxFromBottom + 0.5) * FloorPitch + FloorAnchorOffsetPx;

                    // ----------------------
                    // Innenruf (links, rund)
                    // ----------------------
                    var insideBtn = new Button
                    {
                        Width = InsideSize,
                        Height = InsideSize,
                        Content = f.ToString(),
                        Tag = f,
                        CornerRadius = new CornerRadius(InsideSize / 2),
                        Background = new SolidColorBrush(Color.Parse("#e5e7eb")),
                        Foreground = Brushes.Black,
                        Padding = new Thickness(0),
                        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        FontSize = InsideFontSize
                    };
                    _insideButtons[f] = insideBtn; // f ist das Etagen-Label links am Schacht
                    insideBtn.Click += Innenruf_Click;

                    double xInside = Math.Max(2, shaftL - GapToShaft - InsideSize) + HorizontalOffsetPx;
                    Canvas.SetLeft(insideBtn, xInside);
                    Canvas.SetTop(insideBtn, yCenter - InsideSize / 2.0);
                    FloorButtonsOverlay.Children.Add(insideBtn);

                    // --------------------------
                    // Außenrufe (rechts, STACK)
                    // --------------------------
                    bool isBottom = (f == bootLabel);
                    bool isTop = (f == topLabel);

                    double xRight = shaftR + GapToShaft + HorizontalOffsetPx;

                    if (isBottom)
                    {
                        var up = MakeArrowButton(f, ArrowDir.Up);
                        Canvas.SetLeft(up, xRight);
                        Canvas.SetTop(up, yCenter - ArrowSize / 2.0);
                        FloorButtonsOverlay.Children.Add(up);
                    }
                    else if (isTop)
                    {
                        var down = MakeArrowButton(f, ArrowDir.Down);
                        Canvas.SetLeft(down, xRight);
                        Canvas.SetTop(down, yCenter - ArrowSize / 2.0);
                        FloorButtonsOverlay.Children.Add(down);
                    }
                    else
                    {
                        double stackTotal = ArrowSize * 2 + ArrowStackGap;
                        double yTopArrow = yCenter - (stackTotal / 2.0);

                        var up = MakeArrowButton(f, ArrowDir.Up);
                        var down = MakeArrowButton(f, ArrowDir.Down);

                        Canvas.SetLeft(up, xRight);
                        Canvas.SetTop(up, yTopArrow);

                        Canvas.SetLeft(down, xRight);
                        Canvas.SetTop(down, yTopArrow + ArrowSize + ArrowStackGap);

                        FloorButtonsOverlay.Children.Add(up);
                        FloorButtonsOverlay.Children.Add(down);
                    }
                }


            }
            finally
            {
                _buildingOverlay = false;
            }
        }



        private Button MakeArrowButton(int floorLabel, ArrowDir dir)
        {
            var btn = new Button
            {
                Width = ArrowSize,
                Height = ArrowSize,
                Content = dir == ArrowDir.Up ? "▲" : "▼",
                Tag = (floorLabel, dir),
                CornerRadius = new CornerRadius(ArrowSize / 2), // rund
                Background = new SolidColorBrush(Color.Parse("#e5e7eb")),
                Foreground = Brushes.Black,
                Padding = new Thickness(0),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            btn.Click += Arrow_Click;
            return btn;
        }

        private void Arrow_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is ValueTuple<int, ArrowDir> t)
            {
                int zielLabel = t.Item1;
                var dir = t.Item2;

                int calculatedEtage = (1 + zielLabel) - MonetoringManager.BootFloor;
                if (calculatedEtage < 1) return;
                byte floor = (byte)calculatedEtage;

                byte upDown = dir == ArrowDir.Up ? (byte)0x01 : (byte)0x02;
                SerialPortManager.Instance.SendWithoutResponse(new byte[]
                { 0x04, 0x01, 0x02, upDown, 0x01, floor, 0x01, 0x01 });
            }
        }

        private void Innenruf_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button b && int.TryParse(b.Content?.ToString(), out int zielLabel))
            {
                int calculatedEtage = (1 + zielLabel) - MonetoringManager.BootFloor;
                if (calculatedEtage < 1) return;
                byte floor = (byte)calculatedEtage;

                SerialPortManager.Instance.SendWithoutResponse(new byte[]
                { 0x04, 0x01, 0x05, floor, 0x01, 0x00, 0x01, 0x01 });
            }
        }

        // ===================== Settings / Navigation =====================

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
                            break;
                        case "Testrufe":
                            _cachedTestrufeWindow.Show();
                            _cachedTestrufeWindow.Activate();
                            StopLogic();
                            break;
                        case "Codes":
                            new Code().Show();
                            break;
                        case "SelfDia":
                            var newWindowSelfDia = new MainVertical();
                            newWindowSelfDia.Show();
                            StopLogic();
                            MainWindow.Instance.Close();
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

        private void Button_Click_AutoFollow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _autoFollowMode = AutoFollowMode.Follow;
        }
    }
}
