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
using Avalonia.Animation;
using System.Collections;
using System.Linq;

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
        public int IncrementMultiple;

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

        // Adaptive Framerate
        private const int FrameMsFast = 50;   // ~20 FPS, wie bisher für schnelle Bewegungen
        private const int FrameMsSlow = 16;   // ~60 FPS für langsame Bewegungen

        // Schwellen
        private const double SlowSpeedPxPerSec = 120.0;  // darunter gilt als langsam
        private const double NearTargetPx = 2.0;         // nah am Ziel, dann 60 FPS

        // SmoothDamp Parameter je Modus
        private const double SmoothTimeFast = 0.16; // wie bisher
        private const double SmoothTimeSlow = 0.10; // etwas straffer bei 60 FPS

        // Snap Toleranz je Modus
        private const double SnapEpsFast = 0.3;
        private const double SnapEpsSlow = 0.0;     // kein Snap bei langsamen Bewegungen

        // Laufende Messwerte für Zielgeschwindigkeit
        private double _lastTarget;
        private DateTime _lastTargetSampleTime;
        private bool _slowMode = false;


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

        // ==== HITBOX UND PICKING ====

        // Hitbox vs. sichtbare Größe
        private const double HitboxSize = 56.0;
        private const double VisualInsideSize = 28.0;
        private const double VisualArrowSize = 28.0;

        // maximaler Abstand für Treffer Auswahl des nächsten Ziels
        private const double HitDetectRadius = 32.0;

        // Centers für Nearest Pick
        private readonly Dictionary<int, (Button Btn, Avalonia.Point Center)> _insideCenters = new();
        private readonly Dictionary<(int, ArrowDir), (Button Btn, Avalonia.Point Center)> _arrowCenters = new();

        public int[] LevelIncrement = new int[99];
        public bool firstRide = true;
        public int Fabriknummer;

        private static readonly SolidColorBrush Red = new(Color.Parse("#ef4444"));
        private static readonly SolidColorBrush Yellow = new(Color.Parse("#facc15"));
        private readonly Dictionary<int, TextBlock> tuerZuordnung = new();

        Bitmap Fahrkorb = new Bitmap("Animation/forBuild/Fahrkorb/Fahrkorb.png");
        Bitmap Fahrkorb1offen = new Bitmap("Animation/forBuild/Fahrkorb/Fahrkorb-1offen.png");
        Bitmap Fahrkorb2offen = new Bitmap("Animation/forBuild/Fahrkorb/Fahrkorb-2offen.png");
        Bitmap FahrkorbBoffen = new Bitmap("Animation/forBuild/Fahrkorb/Fahrkorb-Boffen.png");
        Bitmap FahrkorbOverlayoffen1 = new Bitmap("Animation/forBuild/Fahrkorb/offen1.png");
        Bitmap FahrkorbOverlayoffen2 = new Bitmap("Animation/forBuild/Fahrkorb/offen2.png");
        Bitmap FahrkorbOverlayoffen3 = new Bitmap("Animation/forBuild/Fahrkorb/offen3.png");
        Bitmap Fahrkorb1offnetSchliesst = new Bitmap("Animation/forBuild/Fahrkorb/oeffnetSchliesst1.png");
        Bitmap Fahrkorb2offnetSchliesst = new Bitmap("Animation/forBuild/Fahrkorb/offenetschliesst2.png");
        Bitmap Fahrkorb3offnetSchliesst = new Bitmap("Animation/forBuild/Fahrkorb/oeffentschliesst3.png");
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
            // 1) HSE und Monitoring
            HseConnect();
            MonetoringCall();

            // 2) LiveView vorbereiten und SVG rendern
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
                SizeOverlayForShaft();
                // wichtig damit Pointer-Events auf dem Canvas ankommen
                FloorButtonsOverlay.Background = Brushes.Transparent;
                // zentrales Pointer-Handling
                FloorButtonsOverlay.PointerPressed += FloorButtonsOverlay_PointerPressed;

                BuildOrUpdateFloorButtonsOverlay();
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
                    DisplaySpeed();
                    DisplaySignal();
                    DisplayDiff();
                    DisplayDoorSwitch();
                    DisplayDoor();
                    DisplaySKF();
                };
            }
            _updateTimer.Start();

            // Smooth-Follow Fahrkorb → ersetzt durch Feder-Integrator
            if (_animTimer == null)
            {
                _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) }; // immer 60 FPS
                _animClock.Restart();

                _animTimer.Tick += (s, ev) =>
                {
                    double targetRaw = ViewModel.PositionY * MainWindow.Instance.IncrementMultiple;
                    UpdateRawTargetSamples(targetRaw);

                    // echtes dt
                    double dt = Math.Max(_animClock.Elapsed.TotalSeconds, 1e-4);
                    _animClock.Restart();

                    // kontinuierliches Ziel
                    double targetCont = GetContinuousTarget();

                    // adaptives Verhalten: langsam oder schnell
                    double err = targetCont - _springX;
                    double targetSpeed = Math.Abs(_targetSpeedPxPerSec);
                    double halfLife = (targetSpeed < 60 && Math.Abs(err) < 3) ? 0.30 : 0.18;

                    // Feder-Integrator
                    CriticallyDampedSpring(ref _springX, ref _springV, targetCont,
                                           halfLife, 1.0, dt, 20000);

                    _visY = _springX;
                    SetCarTransform(-_visY);

                    UpdateAutoFollowState();
                    if (AutoFollow)
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


        // Stopwatch für dt
        private readonly Stopwatch _animClock = Stopwatch.StartNew();

        // Spring-State
        private double _springX;
        private double _springV;

        // Rohziel-Samples
        private double _rawPrevTarget, _rawCurrTarget;
        private double _rawPrevTime, _rawCurrTime;
        private double _expectedSamplePeriod = 0.10;
        private double _targetSpeedPxPerSec;

        private void UpdateRawTargetSamples(double newTarget)
        {
            double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

            if (Math.Abs(newTarget - _rawCurrTarget) > 0.01)
            {
                double dt = Math.Max(now - _rawCurrTime, 1e-3);
                _expectedSamplePeriod = 0.9 * _expectedSamplePeriod + 0.1 * dt;

                _rawPrevTarget = _rawCurrTarget;
                _rawPrevTime = _rawCurrTime;

                _rawCurrTarget = newTarget;
                _rawCurrTime = now;

                _targetSpeedPxPerSec = (_rawCurrTarget - _rawPrevTarget) / dt;
            }
        }

        private double GetContinuousTarget()
        {
            double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            double span = Math.Max(_rawCurrTime - _rawPrevTime, 1e-3);
            double t = Math.Clamp((now - _rawCurrTime) / Math.Max(_expectedSamplePeriod, 1e-3), 0, 1);
            double s = t * t * (3 - 2 * t); // Smoothstep
            return _rawPrevTarget + (_rawCurrTarget - _rawPrevTarget) * s;
        }

        private static void CriticallyDampedSpring(
            ref double x, ref double v, double target,
            double halfLife, double zeta, double dt, double maxAccel)
        {
            double lambda = Math.Log(2) / Math.Max(halfLife, 1e-4);
            double w = Math.Sqrt(Math.Max(lambda * lambda, 1e-6));

            double a = w * w * (target - x) - 2.0 * zeta * w * v;
            if (maxAccel > 0) a = Math.Clamp(a, -maxAccel, maxAccel);

            v += a * dt;
            x += v * dt;
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

        public void DisplayDoorSwitch()
        {
            if (ViewModel.LS1 == 1)
            {
                DLS1.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }
            else 
            { 
            DLS1.Background = new SolidColorBrush(Color.Parse("#9ca3af"));
            }
            if (ViewModel.LS2 == 1)
            {
                DLS2.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }
            else
            {
                DLS2.Background = new SolidColorBrush(Color.Parse("#9ca3af"));
            }
            if (ViewModel.LS3 == 1)
            {
                DLS3.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }
            else
            {
                DLS3.Background = new SolidColorBrush(Color.Parse("#9ca3af"));
            }


            if(ViewModel.DOP1)
            {
                DOP1.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }
            else
            {
                DOP1.Background = new SolidColorBrush(Color.Parse("#9ca3af"));
            }

            if (ViewModel.DOP2) 
            {
                DOP2.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }
            else
            {
                DOP2.Background = new SolidColorBrush(Color.Parse("#9ca3af"));
            }
            if (ViewModel.DOP3)
            {
                DOP3.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }
            else
            {
                DOP3.Background = new SolidColorBrush(Color.Parse("#9ca3af"));
            }

            if (ViewModel.DCL1)
            {
                DCL1.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }
            else
            {
                DCL1.Background = new SolidColorBrush(Color.Parse("#9ca3af"));

            }

            if (ViewModel.DCL2)
            {
                DCL2.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }
            else
            {
                DCL2.Background = new SolidColorBrush(Color.Parse("#9ca3af"));
            }
            if (ViewModel.DCL3)
            {
                DCL3.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }
            else
            {
                DCL3.Background = new SolidColorBrush(Color.Parse("#9ca3af"));
            }


            if (ViewModel.DREV1) 
            {
                DREV1.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }
            else
            {
                DREV1.Background = new SolidColorBrush(Color.Parse("#9ca3af"));
            }
            if (ViewModel.DREV2)
            {
                DREV2.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }
            else
            {
                DREV2.Background = new SolidColorBrush(Color.Parse("#9ca3af"));
            }
            if (ViewModel.DREV3)
            {
                DREV3.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }
            else
            {
                DREV3.Background = new SolidColorBrush(Color.Parse("#9ca3af"));
            }


           


        }

        public void FollowAnimationOverlay()
        {
            var fahrkorbTransform = ((TransformGroup)FahrkorbImage.RenderTransform)
                        .Children.OfType<TranslateTransform>()
                        .FirstOrDefault();

            var overlayTransform1 = ((TransformGroup)FahrkorbOverlay1.RenderTransform)
                                    .Children.OfType<TranslateTransform>()
                                    .FirstOrDefault();
          

            if (fahrkorbTransform != null && overlayTransform1 != null)
            {
                overlayTransform1.Y = fahrkorbTransform.Y;
                
            }

            var overlayTransform2 = ((TransformGroup)FahrkorbOverlay2.RenderTransform)
                                    .Children.OfType<TranslateTransform>()
                                    .FirstOrDefault();


            if (fahrkorbTransform != null && overlayTransform2 != null)
            {
                overlayTransform2.Y = fahrkorbTransform.Y;

            }

            var overlayTransform3 = ((TransformGroup)FahrkorbOverlay3.RenderTransform)
                                    .Children.OfType<TranslateTransform>()
                                    .FirstOrDefault();


            if (fahrkorbTransform != null && overlayTransform3 != null)
            {
                overlayTransform3.Y = fahrkorbTransform.Y;

            }

        }

        public void DisplaySKF()
        {
            if (ViewModel.SKF == 0)
            {
                VFang.Background = new SolidColorBrush(Color.Parse("#22c55e"));
               
            }
            else
            {
                VFang.Background = new SolidColorBrush(Color.Parse("#9ca3af"));
                
            }
        }

        private TextBlock GetAssignedOrAllocate(int doorIndex, int state)
        {
            if (tuerZuordnung.TryGetValue(doorIndex, out var tb))
                return tb;

            if (state == 0) // bei 0 keinen neuen Slot binden
                return null;

            for (int i = 2; i <= 4; i++)
            {
                var frei = this.FindControl<TextBlock>($"Zustand{i}");
                if (frei != null && string.IsNullOrWhiteSpace(frei.Text))
                {
                    Debug.WriteLine("Slot Frei bei " + i);
                    tuerZuordnung[doorIndex] = frei;
                    return frei;
                }
            }

            Debug.WriteLine("Slots alle belegt.");
            return null;
        }







        public void DisplayDoor()
        {




            if (ViewModel.CurrentStateTueur1 == 48 && ViewModel.CurrentStateTueur2 == 0)
            {
                FahrkorbImage.Source = Fahrkorb1offen;
            }
            else if (ViewModel.CurrentStateTueur1 == 0 && ViewModel.CurrentStateTueur2 == 48)
            {
                FahrkorbImage.Source = Fahrkorb2offen;
            }
            else if (ViewModel.CurrentStateTueur1 == 48 && ViewModel.CurrentStateTueur2 == 48)
            {
                FahrkorbImage.Source = FahrkorbBoffen;
            }
            else if (ViewModel.CurrentStateTueur1 == 0 && ViewModel.CurrentStateTueur2 == 0)
            {
                FahrkorbImage.Source = Fahrkorb;
            }

            if(ViewModel.CurrentStateTueur1 == 48)
            {
                FahrkorbOverlay1.Source = FahrkorbOverlayoffen1;
            }
            if (ViewModel.CurrentStateTueur2 == 48)
            {
                FahrkorbOverlay2.Source = FahrkorbOverlayoffen2;
            }
            if (ViewModel.CurrentStateTueur3 == 48)
            {
                FahrkorbOverlay3.Source = FahrkorbOverlayoffen3;
            }

            if (ViewModel.CurrentStateTueur1 == 0)
            {
                FahrkorbOverlay1.Source = null;
            }
            if (ViewModel.CurrentStateTueur2 == 0)
            {
                FahrkorbOverlay2.Source = null;
            }
            if (ViewModel.CurrentStateTueur3 == 0)
            {
                FahrkorbOverlay3.Source = null;
            }

            if (ViewModel.CurrentStateTueur1 == 80 || ViewModel.CurrentStateTueur1 == 32)
            {
                FahrkorbOverlay1.Source = Fahrkorb1offnetSchliesst;
            }
            if (ViewModel.CurrentStateTueur2 == 80 || ViewModel.CurrentStateTueur1 == 32)
            {
                FahrkorbOverlay2.Source = Fahrkorb2offnetSchliesst;
            }
            if (ViewModel.CurrentStateTueur3 == 80 || ViewModel.CurrentStateTueur1 == 32)
            {
                FahrkorbOverlay3.Source = Fahrkorb3offnetSchliesst;
            }


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
            fahrkorb = fahrkorb - 100000;
            if (fahrkorb < 0) fahrkorb = 0;
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

            int label = MonetoringManager.BootFloor + (index - 1);

            var btn = FindInsideButtonByLabel(label);
            if (btn is null) return;

            SetButtonOutline(btn, zustand == 1);
        }


        private static readonly SolidColorBrush BorderGreen = new(Color.Parse("#22c55e"));
        private static readonly SolidColorBrush BorderGray = new(Color.Parse("#d1d5db"));
        

        private void SetButtonOutline(Button btn, bool active)
        {
            // Outline auf dem kleinen, sichtbaren Border (nicht auf der Hitbox)
            if (btn?.Content is Border circle)
            {
                circle.BorderBrush = active ? BorderGreen : BorderGray;
                circle.BorderThickness = active ? new Thickness(3.0) : new Thickness(1.5);
            }
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
            int upIndex = ViewModel.AufAruftasterquittungEtage;
            int upState = ViewModel.AufAruftasterquittungZustand;
            if (upIndex > 0)
            {
                int upLabel = MonetoringManager.BootFloor + (upIndex - 1);
                var upBtn = FindArrowButtonByLabel(upLabel, ArrowDir.Up);
                if (upBtn != null) SetButtonOutline(upBtn, upState == 1);
            }

            // Abwärts
            int downIndex = ViewModel.AbAruftasterquittungEtage;
            int downState = ViewModel.AbAruftasterquittungZustand;
            if (downIndex > 0)
            {
                int downLabel = MonetoringManager.BootFloor + (downIndex - 1);
                var downBtn = FindArrowButtonByLabel(downLabel, ArrowDir.Down);
                if (downBtn != null) SetButtonOutline(downBtn, downState == 1);
            }
        }

        public void DisplaySpeed()
        {

            Geschwindigkeit.Text = ViewModel.Speed.ToString() + "mm/s";
        }

        public void DisplaySignal()
        {
            if(ViewModel.SGO)
            {
                SGO.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }
            else
            {
                SGO.Background = new SolidColorBrush(Color.Parse("#9ca3af"));
            }

            if (ViewModel.SGU)
            {
                SGU.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }
            else
            {
                SGU.Background = new SolidColorBrush(Color.Parse("#9ca3af"));
            }

            if (ViewModel.SGU)
            {
                SGU.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }
            else
            {
                SGU.Background = new SolidColorBrush(Color.Parse("#9ca3af"));
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

        public void DisplayDiff()
        {
           
            int currentFloor = MainViewModelInstance.RawCurrentFloor;
            int posCalc = MainWindow.Instance.Pos_Cal;
            int fahrkorb = MainViewModelInstance.CurrentFahrkorb / posCalc;
            int zielBund = MainWindow.Instance.LevelIncrement[currentFloor] / posCalc;
            int diff = fahrkorb - zielBund;
            Debug.WriteLine("Diff: " + diff);
            if (diff < 0 && diff > -100000 || diff > 0 && diff < 100000)
            {
                Buendig.Text = diff.ToString() + "mm";
            }
            else
            {
                Buendig.Text = "0mm";
            }
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
                    FollowAnimationOverlay();
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

        public void DisplayLast()
        {
          
            
                Last.Text = ViewModel.CurrentLast.ToString() + "Kg";
            
        }

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

        public static void LevelPositionDefiner()
        {
            int gesamtFloor = HseCom.SendHse(1001);
            for (int i = 0; i < gesamtFloor; i++)
            {

                int etage = i + 1;
                byte ZielEtage = (byte)etage;
                byte[] LevelsPos = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x29, ZielEtage });
                MainWindow.Instance.LevelIncrement[i]  = BitConverter.ToInt32(new byte[] { LevelsPos[10], LevelsPos[11], LevelsPos[12], LevelsPos[13] }, 0);
            }

            
            /*
                        Debug.WriteLine("Level Pos: " + BitConverter.ToString(LevelsPos));
                        int Pos = BitConverter.ToInt32(new byte[] { LevelsPos[10], LevelsPos[11], LevelsPos[12], LevelsPos[13] }, 0);
                        Debug.WriteLine("Level Pos Wert: " + Pos);
            */
        }

        public static void FabrikNummerDefiner()
        {
            byte[] Fabriknummer = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x02 });
            MainWindow.Instance.Fabriknummer = BitConverter.ToInt32(new byte[] { Fabriknummer[10], Fabriknummer[11], Fabriknummer[12], Fabriknummer[13] }, 0);
            Debug.WriteLine("Fabriknummer: " + MainWindow.Instance.Fabriknummer);
        }

        public void setDOPs()
        {
            if (!ViewModel.DOPNA1)
            {
                DOP1.Width = 15;
                DOP1.Height = 5;
                DOP1.CornerRadius = new CornerRadius(0);

                DCL1.Width = 15;
                DCL1.Height = 5;
                DCL1.CornerRadius = new CornerRadius(0);
            }
            else
            {
                DOP1.Width = 25;
                DOP1.Height = 15;
                DOP1.CornerRadius = new CornerRadius(5);

                DCL1.Width = 25;
                DCL1.Height = 15;
                DCL1.CornerRadius = new CornerRadius(5);
            }
            if (!ViewModel.DOPNA2)
            {
                DOP2.Width = 15;
                DOP2.Height = 5;
                DOP2.CornerRadius = new CornerRadius(0);

                DCL2.Width = 15;
                DCL2.Height = 5;
                DCL2.CornerRadius = new CornerRadius(0);
            }
            else
            {
                DOP2.Width = 25;
                DOP2.Height = 15;
                DOP2.CornerRadius = new CornerRadius(5);

                DCL2.Width = 25;
                DCL2.Height = 15;
                DCL2.CornerRadius = new CornerRadius(5);
            }
            if (!ViewModel.DOPNA3)
            {
                DOP3.Width = 15;
                DOP3.Height = 5;
                DOP3.CornerRadius = new CornerRadius(0);

                DCL3.Width = 15;
                DCL3.Height = 5;
                DCL3.CornerRadius = new CornerRadius(0);
            }
            else
            {
                DOP3.Width = 25;
                DOP3.Height = 15;
                DOP3.CornerRadius = new CornerRadius(5);

                DCL1.Width = 25;
                DCL1.Height = 15;
                DCL1.CornerRadius = new CornerRadius(5);
            }
        }

        public void HseConnect()
        {
            Debug.WriteLine("HSECONNECT");
            MonetoringManager.startMonetoring();
            Debug.WriteLine("HSE-Verbindung wird hergestellt...");
            SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x05, 0x01, 0x01 });
            Debug.WriteLine("Monetoring gestartet.");

            MainWindow.Instance.IncrementMultiple = Pos_Cal / 2;

            LevelPositionDefiner();
            FabrikNummerDefiner();
            FN.Text = MainWindow.Instance.Fabriknummer.ToString();
            ViewModel.CurrentZustand = HseCom.SendHse(1005);
            ViewModel.CurrentStateTueur1 = HseCom.SendHse(1006);
            ViewModel.CurrentStateTueur2 = HseCom.SendHse(1016);
            ViewModel.CurrentFahrtZahler = HseCom.SendHse(2145);


            setDOPs();


            byte[] totalDoor = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x20, 0x00 });
            if (totalDoor[10] == 3)
            {
                D1.Foreground = new SolidColorBrush(Colors.White);
                D2.Foreground = new SolidColorBrush(Colors.White);
                D3.Foreground = new SolidColorBrush(Colors.White);
            }
            else 
            {
                D1.Foreground = new SolidColorBrush(Colors.White);
                D2.Foreground = new SolidColorBrush(Colors.White);
                D3.Foreground = new SolidColorBrush(Colors.Transparent);

                DOP3.Height = 0;
                DOP3.Width = 15;
                DOP3.CornerRadius = new CornerRadius(0);

                DCL3.Height = 0;
                DCL3.Width = 15;
                DCL3.CornerRadius = new CornerRadius(0);

                DREV3.Height = 0;
                DREV3.Width = 15;
                DREV3.CornerRadius = new CornerRadius(0);

                DLS3.Height = 0;
                DLS3.Width = 15;
                DLS3.CornerRadius = new CornerRadius(0);

            }


            var transformGroup = (TransformGroup)PositionControl.RenderTransform;
            var YTransform = (TranslateTransform)transformGroup.Children[1];

            int temp = HseCom.SendHse(3001);
            ViewModel.CurrentTemp = temp;

            byte[] last = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x64, 0x80, 0x00 });
            Debug.WriteLine("Last: " + BitConverter.ToString(last));
            
                int ActualLast = BitConverter.ToInt16(new byte[] { last[10], last[11] }, 0);
                Last.Text = ActualLast.ToString() + "Kg";
                
          

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
                _insideCenters.Clear();
                _arrowCenters.Clear();
                return;
            }

            _buildingOverlay = true;
            try
            {
                if (SharedSvgBitmap == null) return;

                int bootLabel = MonetoringManager.BootFloor;
                int count = HseCom.SendHse(1001);
                if (count <= 0) return;
                int topLabel = bootLabel + count - 1;

                var (shaftL, shaftR, shaftTop, shaftBot) = GetShaftFrameInOverlay();

                // ===== Dynamischer Pitch: aus sichtbarer (skalierten) Schachthöhe statt fester Pixelwerte =====
                double shaftHeight = shaftBot - shaftTop;   // sichtbare, skaliert gerenderte Höhe des Schachts
                double pitch = shaftHeight / count;         // Abstand von Etage zu Etage in Overlay-Pixeln
                double firstCenterY = shaftBot - pitch / 2; // Mitte der untersten Etage

                // Neu aufbauen
                FloorButtonsOverlay.Children.Clear();
                _insideButtons.Clear();
                _insideCenters.Clear();
                _arrowCenters.Clear();

                for (int f = bootLabel; f <= topLabel; f++)
                {
                    int idxFromBottom = f - bootLabel;

                    // Vertikale Mitte dieser Etage — unabhängig von der Schachtgröße korrekt
                    double yCenter = firstCenterY - idxFromBottom * pitch;

                    // ===== Innenruf (links) =====
                    // Sichtbares Zentrum: vom linken Schachtrand aus + kleiner horizontaler Offset
                    double insideVisualCenterX = (shaftL - GapToShaft) - (VisualInsideSize / 2.0) + HorizontalOffsetPx;

                    // Hitbox so platzieren, dass der sichtbare Kreis mittig bleibt
                    double insideHitboxLeft = insideVisualCenterX - (HitboxSize / 2.0);
                    double insideHitboxTop = yCenter - (HitboxSize / 2.0);

                    var insideBtn = new Button
                    {
                        Width = HitboxSize,
                        Height = HitboxSize,
                        Padding = new Thickness(0),
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Tag = f,
                        IsHitTestVisible = false // Picking zentral über das Overlay
                    };

                    var insideVisual = new Border
                    {
                        Width = VisualInsideSize,
                        Height = VisualInsideSize,
                        CornerRadius = new CornerRadius(VisualInsideSize / 2),
                        Background = new SolidColorBrush(Color.Parse("#e5e7eb")),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = f.ToString(),
                            FontSize = InsideFontSize,
                            Foreground = Brushes.Black,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        }
                    };

                    insideBtn.Content = insideVisual;
                    Canvas.SetLeft(insideBtn, insideHitboxLeft);
                    Canvas.SetTop(insideBtn, insideHitboxTop);
                    FloorButtonsOverlay.Children.Add(insideBtn);

                    _insideButtons[f] = insideBtn;
                    _insideCenters[f] = (insideBtn, new Avalonia.Point(insideVisualCenterX, yCenter));

                    // ===== Außenruf (rechts) =====
                    bool isBottom = (f == bootLabel);
                    bool isTop = (f == topLabel);

                    // Sichtbares Zentrum rechts + gleicher horizontaler Offset
                    double arrowVisualCenterX = (shaftR + GapToShaft) + (VisualArrowSize / 2.0) + HorizontalOffsetPx;

                    if (isBottom)
                    {
                        // nur ▲
                        double upHitboxLeft = arrowVisualCenterX - (HitboxSize / 2.0);
                        double upHitboxTop = yCenter - (HitboxSize / 2.0);

                        var up = MakeArrowButton(f, ArrowDir.Up);
                        Canvas.SetLeft(up, upHitboxLeft);
                        Canvas.SetTop(up, upHitboxTop);
                        FloorButtonsOverlay.Children.Add(up);

                        _arrowCenters[(f, ArrowDir.Up)] = (up, new Avalonia.Point(arrowVisualCenterX, yCenter));
                    }
                    else if (isTop)
                    {
                        // nur ▼
                        double downHitboxLeft = arrowVisualCenterX - (HitboxSize / 2.0);
                        double downHitboxTop = yCenter - (HitboxSize / 2.0);

                        var down = MakeArrowButton(f, ArrowDir.Down);
                        Canvas.SetLeft(down, downHitboxLeft);
                        Canvas.SetTop(down, downHitboxTop);
                        FloorButtonsOverlay.Children.Add(down);

                        _arrowCenters[(f, ArrowDir.Down)] = (down, new Avalonia.Point(arrowVisualCenterX, yCenter));
                    }
                    else
                    {
                        // Gestapelt: sichtbare Pfeile mit konstantem Abstand, Hitboxen entsprechend mittig
                        double stackVisible = VisualArrowSize + ArrowStackGap;
                        double upCenterY = yCenter - (stackVisible / 2.0);
                        double downCenterY = yCenter + (stackVisible / 2.0);

                        var up = MakeArrowButton(f, ArrowDir.Up);
                        Canvas.SetLeft(up, arrowVisualCenterX - (HitboxSize / 2.0));
                        Canvas.SetTop(up, upCenterY - (HitboxSize / 2.0));
                        FloorButtonsOverlay.Children.Add(up);
                        _arrowCenters[(f, ArrowDir.Up)] = (up, new Avalonia.Point(arrowVisualCenterX, upCenterY));

                        var down = MakeArrowButton(f, ArrowDir.Down);
                        Canvas.SetLeft(down, arrowVisualCenterX - (HitboxSize / 2.0));
                        Canvas.SetTop(down, downCenterY - (HitboxSize / 2.0));
                        FloorButtonsOverlay.Children.Add(down);
                        _arrowCenters[(f, ArrowDir.Down)] = (down, new Avalonia.Point(arrowVisualCenterX, downCenterY));
                    }
                }
            }
            finally
            {
                _buildingOverlay = false;
            }
        }


        private void FloorButtonsOverlay_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            var p = e.GetPosition(FloorButtonsOverlay);

            Button? bestBtn = null;
            double bestDist2 = double.MaxValue;
            Action? invoke = null;

            // Innenruf Kandidaten
            foreach (var kv in _insideCenters)
            {
                int label = kv.Key;
                var (btn, center) = kv.Value;

                double dx = center.X - p.X;
                double dy = center.Y - p.Y;
                double d2 = dx * dx + dy * dy;

                if (d2 < bestDist2)
                {
                    bestDist2 = d2;
                    bestBtn = btn;
                    invoke = () =>
                    {
                        // Direkt dein vorhandenes Sendeformat nutzen
                        int zielLabel = label;
                        int calculatedEtage = (1 + zielLabel) - MonetoringManager.BootFloor;
                        if (calculatedEtage < 1) return;
                        byte floor = (byte)calculatedEtage;
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x04, 0x01, 0x05, floor, 0x01, 0x00, 0x01, 0x01 });
                    };
                }
            }

            // Außenruf Kandidaten
            foreach (var kv in _arrowCenters)
            {
                var key = kv.Key; // (int floor, ArrowDir dir)
                var (btn, center) = kv.Value;

                double dx = center.X - p.X;
                double dy = center.Y - p.Y;
                double d2 = dx * dx + dy * dy;

                if (d2 < bestDist2)
                {
                    bestDist2 = d2;
                    bestBtn = btn;
                    invoke = () =>
                    {
                        int zielLabel = key.Item1;
                        var dir = key.Item2;
                        byte upDown = dir == ArrowDir.Up ? (byte)0x01 : (byte)0x02;

                        int calculatedEtage = (1 + zielLabel) - MonetoringManager.BootFloor;
                        if (calculatedEtage < 1) return;
                        byte floor = (byte)calculatedEtage;

                        SerialPortManager.Instance.SendWithoutResponse(new byte[]
                        { 0x04, 0x01, 0x02, upDown, 0x01, floor, 0x01, 0x01 });
                    };
                }
            }

            // Schwelle damit nicht "irgendwas" auslöst
            if (bestBtn != null && Math.Sqrt(bestDist2) <= HitDetectRadius)
            {
                invoke?.Invoke();
                e.Handled = true;
            }
        }





        private Button MakeArrowButton(int floorLabel, ArrowDir dir)
        {
            var btn = new Button
            {
                Width = HitboxSize,
                Height = HitboxSize,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Tag = (floorLabel, dir),
                IsHitTestVisible = false // zentrales Picking über Overlay
            };

            var arrowVisual = new Border
            {
                Width = VisualArrowSize,
                Height = VisualArrowSize,
                CornerRadius = new CornerRadius(VisualArrowSize / 2),
                Background = new SolidColorBrush(Color.Parse("#e5e7eb")),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = dir == ArrowDir.Up ? "▲" : "▼",
                    FontSize = 16,
                    Foreground = Brushes.Black,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                }
            };

            btn.Content = arrowVisual;
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
                        /*  case "Settings":
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
                              break;*/
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
