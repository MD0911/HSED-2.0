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
        public float IncrementMultiple;
        public int TotalDoor;
        public bool DoorOverlayFinish = false;

        private bool _reconnectPending;

        private bool _lastTerminalActive = false;
        private SerialSettingsWindow? _serialSettingsWindow;


        // SVG Cache
        public static Bitmap SharedSvgBitmap { get; private set; }
        public static Bitmap SharedSvgBitmapAlternative { get; private set; }
        private bool _isLogicInitialized = false;
        private DispatcherTimer _updateTimer;
        private DispatcherTimer _updateTimer2;
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


        // --- Welt->Screen Kalibrierung ---
        private bool _world2ScreenReady = false;
        private double _w2sA = 0.0;   // y_px = _w2sA + _w2sB * world_unit
        private double _w2sB = 1.0;

        // Falls du lieber mit mm arbeiten willst (statt Roh-Encoder):
        // world_unit := encoderCounts oder := mm; beides geht, solange konsistent.
        private bool _useMillimeters = false; // auf true setzen, wenn du mm bevorzugst

        // kleiner Feinkorrektur-Offset, falls die oberste Taste optisch um ein paar Pixel „Luft“ braucht
        private const double TopNudgePx = -6.0; // bei "zu hoch": Richtung 0 bewegen; bei "zu tief": negativer machen



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
        private const double ArrowSize = 28.0; // Außenruf-Pfeile // Abstand zwischen ▲ und ▼ beim Stapeln
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
        Bitmap FahrkorbOneDoor = new Bitmap("Animation/forBuild/Fahrkorb/FahrkorbOneDoor.png");
        Bitmap FahrkorbOneDoorOpen = new Bitmap("Animation/forBuild/Fahrkorb/Fahrkorb1DoorOpen.png");

        private int _lastFloor = int.MinValue;
        private int _lastTemp = int.MinValue;
        private int _lastSpeed = int.MinValue;
        private string? _lastTime;
        private string? _lastDate;

        private IImage? _lastFahrkorbSource;
        private IImage? _lastOverlay1;
        private IImage? _lastOverlay2;
        private IImage? _lastOverlay3;

        private static readonly SolidColorBrush BrushGreen = new(Color.Parse("#22c55e"));
        private static readonly SolidColorBrush BrushGray = new(Color.Parse("#9ca3af"));

        private TranslateTransform? _carTT;
        private TranslateTransform? _o1TT;
        private TranslateTransform? _o2TT;
        private TranslateTransform? _o3TT;

     


        public MainWindow()
        {
            InitializeComponent();
            Instance = this;

            ViewModel = new MainViewModel();
            DataContext = ViewModel;

           // TestrufeService.StartBackgroundUpdate();

            this.Opened += MainWindow_Opened;

          

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
                if (!InitializeLogic())
                    return;
            }
            ResumeLogic();
        }


        private bool InitializeLogic()
        {
            if (!TryPrepareStartupConnection())
            {
                ShowConnectionRecoveryUi();
                return false;
            }

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

            SetupLiveViewScrollBounds();

            // <<<<<<<<<< HIER REIN
            Dispatcher.UIThread.Post(() =>
            {
                CacheOverlayTransforms();
            }, DispatcherPriority.Loaded);

            Dispatcher.UIThread.Post(() =>
            {
                FloorButtonsOverlay.Background = Brushes.Transparent;
            }, DispatcherPriority.Loaded);

            Dispatcher.UIThread.Post(() =>
            {
                BuildFixedFloorButtonsFromAnchor();
                UpdateFixedFloorButtonsVisibility();
            }, DispatcherPriority.Render);

            return true;
        }

        private bool TryPrepareStartupConnection()
        {
            try
            {
                _ = SerialPortManager.Instance;

                Pos_Cal = HseCom.SendHse(10101010);
                gesamteFloors = HseCom.SendHse(1001);

                return Pos_Cal > 0 &&
                       Pos_Cal != 505 &&
                       gesamteFloors > 0 &&
                       gesamteFloors != 505;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Startverbindung konnte nicht vorbereitet werden: " + ex.Message);
                return false;
            }
        }

        public void ShowConnectionRecoveryUi()
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_serialSettingsWindow == null)
                    {
                        _serialSettingsWindow = new SerialSettingsWindow(showConnectionErrorOnOpen: true);
                        _serialSettingsWindow.Closed += (_, __) => _serialSettingsWindow = null;
                        _serialSettingsWindow.Show(this);
                    }
                    else
                    {
                        if (!_serialSettingsWindow.IsVisible)
                            _serialSettingsWindow.Show(this);

                        _serialSettingsWindow.ShowConnectionErrorPopup();
                        _serialSettingsWindow.Activate();
                    }

                    _serialSettingsWindow.Topmost = true;
                    _serialSettingsWindow.Topmost = false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Fehler beim Anzeigen der Verbindungshilfe: " + ex.Message);
                }
            }, DispatcherPriority.Loaded);
        }

        public void HideConnectionRecoveryPopup()
        {
            Dispatcher.UIThread.Post(() =>
            {
                _serialSettingsWindow?.HideConnectionErrorPopup();
            }, DispatcherPriority.Loaded);
        }


        private void CacheOverlayTransforms()
        {
            _carTT = ((TransformGroup)FahrkorbImage.RenderTransform).Children[1] as TranslateTransform;
            _o1TT = ((TransformGroup)FahrkorbOverlay1.RenderTransform).Children[1] as TranslateTransform;
            _o2TT = ((TransformGroup)FahrkorbOverlay2.RenderTransform).Children[1] as TranslateTransform;
            _o3TT = ((TransformGroup)FahrkorbOverlay3.RenderTransform).Children[1] as TranslateTransform;
        }

        private double EncToWorld(int enc)
        {
            if (_useMillimeters)
            {
                // Roh-Encoder -> mm
                // Pos_Cal = Impulse pro mm (dein Code nutzt CurrentFahrkorb / Pos_Cal)
                return enc / (double)Pos_Cal;
            }
            else
            {
                // direkt Roh-Encoder als "world"
                return enc;
            }
        }

        // Welt (mm oder Encoder) -> Screen-Pixel
        private double WorldToScreenY(double world)
        {
            return _w2sA + _w2sB * world;
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

        // --- Dein fester Top-Anker & Pitch ---
        private const double TopAnchorY = 155.0;  // hier sitzt die OBERSTE Etage (Mitte des Innenruf-Kreises)
        private const double Pitch = 95.0;   // fester Abstand Etage->Etage

        // horizontale Positionen (kannst du anpassen)
        private const double InsideCenterX = 18.5;   // links (Innenruf)
        private const double ArrowCenterX = 202.5;  // rechts (Außenruf)

        private const double InsideVisualSize = 28.0;
        private const double ArrowVisualSize = 28.0;
        private const double HitboxSizeFixed = 56.0;
        private const double ArrowStackGap = 6.0;

        // Etikettenbereich (nur für Sichtbarkeit/Clamps)
        private const int MinFloorLabel = -20;
        private const int MaxFloorLabel = 20;

        private readonly Dictionary<int, Button> _fixedInsideByLabel = new();
        private readonly Dictionary<(int, ArrowDir), Button> _fixedArrowByLabel = new();

        private static int DisplayLabelToFloorIndex(int label)
        {
            return (label - MonetoringManager.BootFloor) + 1;
        }

        private static int FloorIndexToDisplayLabel(int floorIndex1Based)
        {
            return MonetoringManager.BootFloor + (floorIndex1Based - 1);
        }

        private static int GetBottomFloorIndex1Based()
        {
            byte[] response = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x00, 0x00, 0x03 });
            if (response == null || response.Length <= 10)
                return 1;

            return response[10] + 1;
        }

        private static byte[] GetInsideCallDoorBytes(int floorIndex1Based)
        {
            // DOORPOS (0x2406): Bit 0 = Tür 1, Bit 1 = Tür 2, Bit 2 = Tür 3
            byte[] response = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x06, (byte)floorIndex1Based, 0x05 });
            if (response == null || response.Length <= 10)
                return new byte[] { 0x01 };

            byte doorMask = response[10];
            var doorBytes = new List<byte>(2);

            if ((doorMask & 0x01) != 0)
                doorBytes.Add(0x01);

            if ((doorMask & 0x02) != 0)
                doorBytes.Add(0x02);

            if (doorBytes.Count == 0)
                doorBytes.Add(0x01);

            return doorBytes.ToArray();
        }

        private (int boot, int count, int top) GetFloorRange()
        {
            int firstFloorIndex = GetBottomFloorIndex1Based();
            int count = HseCom.SendHse(1001);
            if (count < 1) count = 1;

            int lastFloorIndex = firstFloorIndex + count - 1;
            int boot = FloorIndexToDisplayLabel(firstFloorIndex);
            int top = FloorIndexToDisplayLabel(lastFloorIndex);

            // clamp in unseren sichtbaren Labelbereich
            if (boot < MinFloorLabel) { int d = MinFloorLabel - boot; boot += d; top += d; }
            if (top > MaxFloorLabel) { int d = top - MaxFloorLabel; boot -= d; top -= d; }

            boot = Math.Clamp(boot, MinFloorLabel, MaxFloorLabel);
            top = Math.Clamp(top, MinFloorLabel, MaxFloorLabel);
            count = Math.Max(1, top - boot + 1);
            return (boot, count, top);
        }

        private Button MakeFixedArrowButton(int floorLabel, ArrowDir dir)
        {
            var btn = new Button
            {
                Width = HitboxSizeFixed,
                Height = HitboxSizeFixed,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Tag = (floorLabel, dir)
            };

            var arrowVisual = new Border
            {
                Width = ArrowVisualSize,
                Height = ArrowVisualSize,
                CornerRadius = new CornerRadius(ArrowVisualSize / 2),
                Background = new SolidColorBrush(Color.Parse("#e5e7eb")),
                BorderBrush = new SolidColorBrush(Color.Parse("#d1d5db")),
                BorderThickness = new Thickness(1.5),
                Child = new TextBlock
                {
                    Text = dir == ArrowDir.Up ? "▲" : "▼",
                    FontSize = 16,
                    Foreground = Brushes.Black,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                },
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            btn.Content = arrowVisual;
            btn.Click += Aussenruf_Click_Fixed;
            return btn;
        }

        private static void SetSourceIfChanged(Image img, ref IImage? cache, IImage? next)
        {
            if (ReferenceEquals(cache, next)) return;
            cache = next;
            img.Source = next;
        }


        private void BuildFixedFloorButtonsFromAnchor()
        {
            FloorButtonsOverlay.Children.Clear();
            _fixedInsideByLabel.Clear();
            _fixedArrowByLabel.Clear();

            var (boot, count, top) = GetFloorRange();

            // von oben nach unten (top -> boot)
            for (int label = top; label >= boot; label--)
            {
                int row = top - label;                 // 0,1,2,...
                double yCenter = TopAnchorY + row * Pitch;

                // Innenruf (runde Taste)
                var insideBtn = new Button
                {
                    Width = HitboxSizeFixed,
                    Height = HitboxSizeFixed,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    Tag = label
                };

                var insideVisual = new Border
                {
                    Width = InsideVisualSize,
                    Height = InsideVisualSize,
                    CornerRadius = new CornerRadius(InsideVisualSize / 2),
                    Background = new SolidColorBrush(Color.Parse("#e5e7eb")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#d1d5db")),
                    BorderThickness = new Thickness(1.5),
                    Child = new TextBlock
                    {
                        Text = label.ToString(),
                        FontSize = 12,
                        Foreground = Brushes.Black,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    },
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };

                insideBtn.Content = insideVisual;
                insideBtn.Click += Innenruf_Click_Fixed;

                Canvas.SetLeft(insideBtn, InsideCenterX - HitboxSizeFixed / 2);
                Canvas.SetTop(insideBtn, yCenter - HitboxSizeFixed / 2);
                FloorButtonsOverlay.Children.Add(insideBtn);
                _fixedInsideByLabel[label] = insideBtn;

                // Außenruf-Pfeile (oben nur ▼, unten nur ▲, sonst gestapelt)
                bool isTop = (label == top);
                bool isBottom = (label == boot);

                if (isTop)
                {
                    var downBtn = MakeFixedArrowButton(label, ArrowDir.Down);
                    Canvas.SetLeft(downBtn, ArrowCenterX - HitboxSizeFixed / 2);
                    Canvas.SetTop(downBtn, yCenter - HitboxSizeFixed / 2);
                    FloorButtonsOverlay.Children.Add(downBtn);
                    _fixedArrowByLabel[(label, ArrowDir.Down)] = downBtn;
                }
                else if (isBottom)
                {
                    var upBtn = MakeFixedArrowButton(label, ArrowDir.Up);
                    Canvas.SetLeft(upBtn, ArrowCenterX - HitboxSizeFixed / 2);
                    Canvas.SetTop(upBtn, yCenter - HitboxSizeFixed / 2);
                    FloorButtonsOverlay.Children.Add(upBtn);
                    _fixedArrowByLabel[(label, ArrowDir.Up)] = upBtn;
                }
                else
                {
                    double stack = ArrowVisualSize + ArrowStackGap;
                    double upY = yCenter - stack / 2.0;
                    double dnY = yCenter + stack / 2.0;

                    var upBtn = MakeFixedArrowButton(label, ArrowDir.Up);
                    Canvas.SetLeft(upBtn, ArrowCenterX - HitboxSizeFixed / 2);
                    Canvas.SetTop(upBtn, upY - HitboxSizeFixed / 2);
                    FloorButtonsOverlay.Children.Add(upBtn);
                    _fixedArrowByLabel[(label, ArrowDir.Up)] = upBtn;

                    var dnBtn = MakeFixedArrowButton(label, ArrowDir.Down);
                    Canvas.SetLeft(dnBtn, ArrowCenterX - HitboxSizeFixed / 2);
                    Canvas.SetTop(dnBtn, dnY - HitboxSizeFixed / 2);
                    FloorButtonsOverlay.Children.Add(dnBtn);
                    _fixedArrowByLabel[(label, ArrowDir.Down)] = dnBtn;
                }
            }
        }



        public void ForceRefreshOverlaidUi()
        {
            // Cache invalidieren, damit Display-Methoden wirklich neu setzen
            _lastTemp = int.MinValue;
            _lastSpeed = int.MinValue;
            _lastFloor = int.MinValue;
            _lastTime = null;
            _lastDate = null;

            // Einmal alles aktualisieren, was sonst pausiert wird
            DisplayTemp();
            DisplayLast();
            DisplayFahrtZahler();
            DisplayBStunden();
            DisplayFahrkorbMM();
            DisplayDatum();
            DisplayUhr();
            DisplaySpeed();
            DisplayDiff();
        }




        private void ResumeLogic()
        {
            _monetoringManager?.Start();

            if (_updateTimer == null)
            {
                _updateTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };

                _updateTimer.Tick += (s, ev) =>
                {
                    // Diese UI-Updates sind IMMER notwendig
                    DisplayFloor();
                    DisplayZustand();
                    DisplaySk();
                    DisplayInnenruftasterquittung();
                    DisplayAussenruftasterquittung();
                    DisplaySignal();
                    DisplayDoorSwitch();
                    DisplayDoor();
                    DisplaySKF();

                    if (!DoorOverlayFinish)
                    {
                        FollowAnimationOverlay();
                    }

                    bool terminalActive = TerminalManager.terminalActive;

                    // Terminal wurde gerade geschlossen → einmal alles nachziehen
                    if (_lastTerminalActive && !terminalActive)
                    {
                        ForceRefreshOverlaidUi();
                    }

                    _lastTerminalActive = terminalActive;

                    // Alles was vom Terminal verdeckt ist, nur updaten wenn Terminal zu
                    if (!terminalActive)
                    {
                        DisplayTemp();
                        DisplayLast();
                        DisplayFahrtZahler();
                        DisplayBStunden();
                        DisplayFahrkorbMM();
                        DisplayDatum();
                        DisplayUhr();
                        DisplaySpeed();
                        DisplayDiff();
                    }
                };
            }

            _updateTimer.Start();

            // Animation läuft IMMER weiter
            if (_animTimer == null)
            {
                _animTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };

                _animClock.Restart();

                _animTimer.Tick += (s, ev) =>
                {
                    double targetRaw = ViewModel.PositionY;
                    UpdateRawTargetSamples(targetRaw);

                    double dt = Math.Max(_animClock.Elapsed.TotalSeconds, 1e-4);
                    _animClock.Restart();

                    double targetCont = GetContinuousTarget();

                    CriticallyDampedSpring(
                        ref _springX,
                        ref _springV,
                        targetCont,
                        0.18,
                        1.0,
                        dt,
                        20000
                    );

                    _visY = _springX;
                    SetCarTransform(-_visY);

                    UpdateAutoFollowState();
                    if (AutoFollow)
                        AutoFollowIfNeeded();
                };
            }

            _animTimer.Start();

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
            DLS1.Background = ViewModel.LS1 == 1 ? BrushGreen : BrushGray;
            DLS2.Background = ViewModel.LS2 == 1 ? BrushGreen : BrushGray;
            DLS3.Background = ViewModel.LS3 == 1 ? BrushGreen : BrushGray;

            DOP1.Background = ViewModel.DOP1 ? BrushGreen : BrushGray;
            DOP2.Background = ViewModel.DOP2 ? BrushGreen : BrushGray;
            DOP3.Background = ViewModel.DOP3 ? BrushGreen : BrushGray;

            DCL1.Background = ViewModel.DCL1 ? BrushGreen : BrushGray;
            DCL2.Background = ViewModel.DCL2 ? BrushGreen : BrushGray;
            DCL3.Background = ViewModel.DCL3 ? BrushGreen : BrushGray;

            DREV1.Background = ViewModel.DREV1 ? BrushGreen : BrushGray;
            DREV2.Background = ViewModel.DREV2 ? BrushGreen : BrushGray;
            DREV3.Background = ViewModel.DREV3 ? BrushGreen : BrushGray;
        }




        public void FollowAnimationOverlay()
        {
            if (_carTT == null || _o1TT == null || _o2TT == null || _o3TT == null)
                return;

            var y = _carTT.Y;
            _o1TT.Y = y;
            _o2TT.Y = y;
            _o3TT.Y = y;
        }


        public void DisplaySKF()
        {
            if (ViewModel.SKFActive)
            {
                VFang.CornerRadius = new CornerRadius(5);
                VFang.Height = 15;
                VFang.Width = 25;
                VFang.Margin = new Thickness(0, 0, 0, 0);

                VFang.Background = (ViewModel.SKF == 1) ? BrushGreen : BrushGray;
            }
            else
            {
                VFang.Margin = new Thickness(0, 5, 0, 0);
                VFang.Height = 5;
                VFang.Width = 15;
                VFang.CornerRadius = new CornerRadius(0);
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
            IImage? nextCar = null;
            IImage? nextO1 = null;
            IImage? nextO2 = null;
            IImage? nextO3 = null;

            // --- Fahrkorb Basis ---
            if (TotalDoor != 1)
            {
                if (ViewModel.CurrentStateTueur1 == 48 && ViewModel.CurrentStateTueur2 == 0) nextCar = Fahrkorb1offen;
                else if (ViewModel.CurrentStateTueur1 == 0 && ViewModel.CurrentStateTueur2 == 48) nextCar = Fahrkorb2offen;
                else if (ViewModel.CurrentStateTueur1 == 48 && ViewModel.CurrentStateTueur2 == 48) nextCar = FahrkorbBoffen;
                else nextCar = Fahrkorb;
            }
            else
            {
                nextCar = (ViewModel.CurrentStateTueur1 == 48) ? FahrkorbOneDoorOpen : FahrkorbOneDoor;
            }

            // --- Overlays ---
            nextO1 = GetDoorOverlay(ViewModel.CurrentStateTueur1, FahrkorbOverlayoffen1, Fahrkorb1offnetSchliesst);
            nextO2 = GetDoorOverlay(ViewModel.CurrentStateTueur2, FahrkorbOverlayoffen2, Fahrkorb2offnetSchliesst);
            nextO3 = GetDoorOverlay(ViewModel.CurrentStateTueur3, FahrkorbOverlayoffen3, Fahrkorb3offnetSchliesst);

            // nur setzen wenn changed
            SetSourceIfChanged(FahrkorbImage, ref _lastFahrkorbSource, nextCar);
            SetSourceIfChanged(FahrkorbOverlay1, ref _lastOverlay1, nextO1);
            SetSourceIfChanged(FahrkorbOverlay2, ref _lastOverlay2, nextO2);
            SetSourceIfChanged(FahrkorbOverlay3, ref _lastOverlay3, nextO3);
        }

        private static IImage? GetDoorOverlay(int state, IImage offen, IImage bewegt)
        {
            // 48 offen, 80/32 bewegt, 0 sonst aus
            if (state == 48) return offen;
            if (state == 80 || state == 32) return bewegt;
            return null;
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
            int posCal = Pos_Cal;
            if (posCal <= 0)
            {
                Hoehe.Text = "0mm";
                return;
            }

            int[] levels = LievViewManager.IngrementEtage;
            int bottomLevel = 0;

            if (levels != null && levels.Length > 0)
            {
                var validLevels = levels.Where(level => level != 0).ToArray();
                if (validLevels.Length > 0)
                {
                    bottomLevel = validLevels.Min();
                }
                else
                {
                    bottomLevel = levels.Min();
                }
            }

            int relativeMm = (ViewModel.CurrentFahrkorb - bottomLevel) / posCal;
            if (relativeMm < 0) relativeMm = 0;

            Hoehe.Text = relativeMm.ToString() + "mm";
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
            // Aufwärts-Quittung
            int upIndex = ViewModel.AufAruftasterquittungEtage;
            int upState = ViewModel.AufAruftasterquittungZustand;
            if (upIndex > 0)
            {
                int upLabel = FloorIndexToDisplayLabel(upIndex);
                if (_fixedArrowByLabel.TryGetValue((upLabel, ArrowDir.Up), out var upBtn) && upBtn.Content is Border upCircle)
                    SetButtonOutlineFixed(upCircle, HasAnyActiveCallAck(upState));
            }

            // Abwärts-Quittung
            int downIndex = ViewModel.AbAruftasterquittungEtage;
            int downState = ViewModel.AbAruftasterquittungZustand;
            if (downIndex > 0)
            {
                int downLabel = FloorIndexToDisplayLabel(downIndex);
                if (_fixedArrowByLabel.TryGetValue((downLabel, ArrowDir.Down), out var downBtn) && downBtn.Content is Border downCircle)
                    SetButtonOutlineFixed(downCircle, HasAnyActiveCallAck(downState));
            }
        }


        public void DisplaySpeed()
        {
            int s = ViewModel.Speed;
            if (s == _lastSpeed) return;
            _lastSpeed = s;
            Geschwindigkeit.Text = s + "mm/s";
        }

        public void DisplaySignal()
        {
            SGO.Background = ViewModel.SGO ? BrushGreen : BrushGray;
            SGU.Background = ViewModel.SGU ? BrushGreen : BrushGray;
            SGM.Background = ViewModel.SGM ? BrushGreen : BrushGray;
        }






        public void DisplayDatum()
        {
            var v = ViewModel.CurrentDate;
            if (v == _lastDate) return;
            _lastDate = v;
            Datum.Text = v;
        }
        public void DisplayUhr()
        {
            var v = ViewModel.CurrentTime;
            if (v == _lastTime) return;
            _lastTime = v;
            Uhr.Text = v;
        }
        public void DisplayTemp()
        {
            int t = ViewModel.CurrentTemp;
            if (t == _lastTemp) return;
            _lastTemp = t;
            Temp.Text = t + "°C";
        }
        public void DisplayFloor()
        {
            int f = ViewModel.CurrentFloor;
            if (f == _lastFloor) return;
            _lastFloor = f;
            Etage.Text = f.ToString();
        }

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
                    DoorOverlayFinish = true;
                    break;
                case 5:
                    Zustand.Text = "Fährt";
                    Zustand.Foreground = new SolidColorBrush(Color.Parse("#22c55e"));
                    DoorOverlayFinish = true;
                    break;
                case 6:
                    Zustand.Text = "Einfahrt";
                    Zustand.Foreground = new SolidColorBrush(Color.Parse("#facc15"));
                    DoorOverlayFinish = false;
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
            SK1.Background = ViewModel.CurrentSK1 == 0 ? BrushGray : BrushGreen;
            SK2.Background = ViewModel.CurrentSK2 == 0 ? BrushGray : BrushGreen;
            SK3.Background = ViewModel.CurrentSK3 == 0 ? BrushGray : BrushGreen;
            SK4.Background = ViewModel.CurrentSK4 == 0 ? BrushGray : BrushGreen;
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

        private (int boot, int count, int top) GetFloorRangeSafe()
        {
            int firstFloorIndex = GetBottomFloorIndex1Based();

            // Etagenanzahl vom HSE
            int count = HseCom.SendHse(1001);

            // Sanity-Checks: sinnvolle Grenzen setzen
            // Dein fixes Raster ist -20..+20 -> max. 41 Etagen darstellbar
            const int MIN_COUNT = 1;
            const int MAX_COUNT = 41; // entspricht -20..+20

            if (count < MIN_COUNT || count > 200) // 200 = "absurd groß" = Messfehler
                count = 10;                       // Fallback: 10 Etagen

            // Auf darstellbaren Bereich clampen
            count = Math.Clamp(count, MIN_COUNT, MAX_COUNT);

            // Wenn Boot außerhalb unseres fixen Rasters liegt, auf -20..+20 begrenzen
            const int MIN_LABEL = -20;
            const int MAX_LABEL = 20;

            int boot = FloorIndexToDisplayLabel(firstFloorIndex);
            int top = boot + count - 1;

            // Falls boot so groß/klein ist, dass (boot..top) außerhalb fällt,
            // schieben wir den Bereich, damit er ins Raster passt.
            if (boot < MIN_LABEL)
            {
                // nach oben schieben
                int delta = MIN_LABEL - boot;
                boot += delta;
                top += delta;
            }
            if (top > MAX_LABEL)
            {
                // nach unten schieben
                int delta = top - MAX_LABEL;
                boot -= delta;
                top -= delta;
            }

            // final in Raster einpassen
            boot = Math.Clamp(boot, MIN_LABEL, MAX_LABEL);
            top = Math.Clamp(top, MIN_LABEL, MAX_LABEL);

            // Count ggf. erneut ableiten
            count = Math.Clamp(top - boot + 1, MIN_COUNT, MAX_COUNT);

            return (boot, count, top);
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
                DOP1.Width = 15; DOP1.Height = 5; DOP1.CornerRadius = new CornerRadius(0);
                DCL1.Width = 15; DCL1.Height = 5; DCL1.CornerRadius = new CornerRadius(0);
            }
            else
            {
                DOP1.Width = 25; DOP1.Height = 15; DOP1.CornerRadius = new CornerRadius(5);
                DCL1.Width = 25; DCL1.Height = 15; DCL1.CornerRadius = new CornerRadius(5);
            }

            if (!ViewModel.DOPNA2)
            {
                DOP2.Width = 15; DOP2.Height = 5; DOP2.CornerRadius = new CornerRadius(0);
                DCL2.Width = 15; DCL2.Height = 5; DCL2.CornerRadius = new CornerRadius(0);
            }
            else
            {
                DOP2.Width = 25; DOP2.Height = 15; DOP2.CornerRadius = new CornerRadius(5);
                DCL2.Width = 25; DCL2.Height = 15; DCL2.CornerRadius = new CornerRadius(5);
            }

            if (!ViewModel.DOPNA3)
            {
                DOP3.Width = 15; DOP3.Height = 5; DOP3.CornerRadius = new CornerRadius(0);
                DCL3.Width = 15; DCL3.Height = 5; DCL3.CornerRadius = new CornerRadius(0);
            }
            else
            {
                DOP3.Width = 25; DOP3.Height = 15; DOP3.CornerRadius = new CornerRadius(5);
                DCL3.Width = 25; DCL3.Height = 15; DCL3.CornerRadius = new CornerRadius(5); // FIX
            }
        }

        public async void HseConnect()
        {
            Debug.WriteLine("HSECONNECT");
            MonetoringManager.startMonetoring();
            Debug.WriteLine("HSE-Verbindung wird hergestellt...");
            SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x05, 0x01, 0x01 });
            Debug.WriteLine("Monetoring gestartet.");

            MainWindow.Instance.IncrementMultiple = 2 / Pos_Cal;

            LevelPositionDefiner();
            FabrikNummerDefiner();
            FN.Text = MainWindow.Instance.Fabriknummer.ToString();

            ViewModel.CurrentZustand = HseCom.SendHse(1005);
            ViewModel.CurrentStateTueur1 = HseCom.SendHse(1006);
            ViewModel.CurrentStateTueur2 = HseCom.SendHse(1016);
            ViewModel.CurrentFahrtZahler = HseCom.SendHse(2145);
            ViewModel.SKFActive = HseCom.SendHse(2653) == 1;

            setDOPs();

            byte[] totalDoor = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x20, 0x00 });
            MainWindow.Instance.TotalDoor = totalDoor[10];

            if (totalDoor[10] == 3)
            {
                D1.Foreground = new SolidColorBrush(Colors.White);
                D2.Foreground = new SolidColorBrush(Colors.White);
                D3.Foreground = new SolidColorBrush(Colors.White);
            }
            else if (totalDoor[10] == 2)
            {
                D1.Foreground = new SolidColorBrush(Colors.White);
                D2.Foreground = new SolidColorBrush(Colors.White);
                D3.Foreground = new SolidColorBrush(Colors.Transparent);

                DOP3.Height = 0; DOP3.Width = 15; DOP3.CornerRadius = new CornerRadius(0);
                DCL3.Height = 0; DCL3.Width = 15; DCL3.CornerRadius = new CornerRadius(0);
                DREV3.Height = 0; DREV3.Width = 15; DREV3.CornerRadius = new CornerRadius(0);
                DLS3.Height = 0; DLS3.Width = 15; DLS3.CornerRadius = new CornerRadius(0);
            }
            else if (totalDoor[10] == 1)
            {
                D1.Foreground = new SolidColorBrush(Colors.White);
                D2.Foreground = new SolidColorBrush(Colors.Transparent);
                D3.Foreground = new SolidColorBrush(Colors.Transparent);

                DOP2.Height = 0; DOP2.Width = 15; DOP2.CornerRadius = new CornerRadius(0);
                DCL2.Height = 0; DCL2.Width = 15; DCL2.CornerRadius = new CornerRadius(0);
                DREV2.Height = 0; DREV2.Width = 15; DREV2.CornerRadius = new CornerRadius(0);
                DLS2.Height = 0; DLS2.Width = 15; DLS2.CornerRadius = new CornerRadius(0);

                DOP3.Height = 0; DOP3.Width = 15; DOP3.CornerRadius = new CornerRadius(0);
                DCL3.Height = 0; DCL3.Width = 15; DCL3.CornerRadius = new CornerRadius(0);
                DREV3.Height = 0; DREV3.Width = 15; DREV3.CornerRadius = new CornerRadius(0);
                DLS3.Height = 0; DLS3.Width = 15; DLS3.CornerRadius = new CornerRadius(0);
            }

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

            // Fix 1: Keine Rekursion mehr
            if (currentFloor == 505)
            {
                if (_reconnectPending) return;
                _reconnectPending = true;

                await Task.Delay(1000);

                _reconnectPending = false;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    HseConnect();
                });

                return;
            }

            ViewModel.CurrentFloor = currentFloor;

            _monetoringManager = new MonetoringManager();
            _monetoringManager.Start();
            Debug.WriteLine("HSE-Connect abgeschlossen");

            int bStunden = MonetoringManager.Betriebsstunden;

            // Fix 2: float kann nicht null sein, daher sauber prüfen
            float lastKorbPosition = MonetoringManager.LastKorbPosition;
            if (float.IsNaN(lastKorbPosition) || float.IsInfinity(lastKorbPosition))
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

            Dispatcher.UIThread.Post(() =>
            {
                BuildFixedFloorButtonsFromAnchor();
                UpdateFixedFloorButtonsVisibility();
            }, DispatcherPriority.Render);
        }

        private bool IsValidPosition(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }


        private void UpdateFixedFloorButtonsVisibility()
        {
            var (boot, count, top) = GetFloorRange();

            for (int label = MinFloorLabel; label <= MaxFloorLabel; label++)
            {
                bool inRange = (label >= boot && label <= top);

                if (_fixedInsideByLabel.TryGetValue(label, out var inside))
                    inside.IsVisible = inRange;

                if (_fixedArrowByLabel.TryGetValue((label, ArrowDir.Up), out var upBtn))
                    upBtn.IsVisible = inRange && (label != top);    // oberste: kein ▲

                if (_fixedArrowByLabel.TryGetValue((label, ArrowDir.Down), out var downBtn))
                    downBtn.IsVisible = inRange && (label != boot); // unterste: kein ▼
            }
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
            // Position der beiden Schacht-Canvas-Anfänge im Overlay
            var p1 = PositionControl.TranslatePoint(new Avalonia.Point(0, 0), FloorButtonsOverlay) ?? new Avalonia.Point(0, 0);
            var p2 = PositionControl2.TranslatePoint(new Avalonia.Point(0, 0), FloorButtonsOverlay) ?? new Avalonia.Point(0, 0);

            // Sicht-Skalierung des Schachts (ScaleTransform an PositionControl)
            double scale = GetSchachtScale(); // i.d.R. 0.5

            // Die SVGs werden innerhalb der Canvas per TranslateTransform nach unten verschoben (197.5).
            // Dieses innere Offset muss in top/bottom einfließen!
            double imgOffset1 = 0.0;
            double imgOffset2 = 0.0;

            try
            {
                var tg1 = (TransformGroup)SvgImageControlAlternative.RenderTransform;
                imgOffset1 = ((TranslateTransform)tg1.Children[1]).Y; // 197.5
            }
            catch { /* fallback 0 */ }

            try
            {
                var tg2 = (TransformGroup)SvgImageControl.RenderTransform;
                imgOffset2 = ((TranslateTransform)tg2.Children[1]).Y; // 197.5
            }
            catch { /* fallback 0 */ }

            // Sichtbare Breite/Höhe der gerenderten SVGs
            // Achtung: Jedes Canvas zeigt ein SharedSvgBitmap (gleiche Höhe); insgesamt zwei Segmente übereinander.
            int bmpH = SharedSvgBitmap?.PixelSize.Height ?? 0;
            double visualH = bmpH * scale;
            double visualW = PositionControl.Bounds.Width * scale; // 300 * 0.5 = 150

            // Effektive sichtbare Top/Bottom je Segment **inkl.** innerem Image-Offset
            double seg1Top = p1.Y + imgOffset1 * scale;
            double seg1Bottom = seg1Top + visualH;

            double seg2Top = p2.Y + imgOffset2 * scale;
            double seg2Bottom = seg2Top + visualH;

            // Gesamter Schacht-Rahmen über beide Segmente
            double top = Math.Min(seg1Top, seg2Top);
            double bottom = Math.Max(seg1Bottom, seg2Bottom);

            // Links/Rechts aus dem ersten Canvas abgeleitet (beide haben gleiche Breite/Ausrichtung)
            double left = p1.X;
            double right = left + visualW;

            return (left, right, top, bottom);
        }


        private sealed class FloorItem
        {
            public int Label { get; set; }       // z.B. -1, 0, 1, 2, ...
            public bool IsTop { get; set; }      // oberste Etage?
            public bool IsBottom { get; set; }   // unterste Etage?
            public bool ShowUp => !IsTop;        // ▲ sichtbar außer ganz oben
            public bool ShowDown => !IsBottom;   // ▼ sichtbar außer ganz unten
        }






        private void Innenruf_Click_Fixed(object? sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is int zielLabel)
            {
                int floorIndex1Based = DisplayLabelToFloorIndex(zielLabel);
                if (floorIndex1Based < 1) return;
                byte floor = (byte)floorIndex1Based;
                byte[] doors = GetInsideCallDoorBytes(floorIndex1Based);

                foreach (byte door in doors)
                {
                    SerialPortManager.Instance.SendWithoutResponse(new byte[]
                    { 0x04, 0x01, 0x05, floor, 0x01, 0x00, door, 0x01 });
                }
            }
        }

        private void Aussenruf_Click_Fixed(object? sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is ValueTuple<int, ArrowDir> t)
            {
                int zielLabel = t.Item1;
                var dir = t.Item2;

                int floorIndex1Based = DisplayLabelToFloorIndex(zielLabel);
                if (floorIndex1Based < 1) return;
                byte floor = (byte)floorIndex1Based;

                byte upDown = dir == ArrowDir.Up ? (byte)0x01 : (byte)0x02;
                SerialPortManager.Instance.SendWithoutResponse(new byte[]
                { 0x04, 0x01, 0x02, upDown, 0x01, floor, 0x01, 0x01 });
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

        private static readonly SolidColorBrush BorderGreen = new(Color.Parse("#22c55e"));
        private static readonly SolidColorBrush BorderGray = new(Color.Parse("#d1d5db"));

        private static bool HasAnyActiveCallAck(int state)
        {
            // Rufquittungen werden als Bitfeld pro Tür übertragen.
            return (state & 0xFF) != 0;
        }

        private void SetButtonOutlineFixed(Border circle, bool active)
        {
            circle.BorderBrush = active ? BorderGreen : BorderGray;
            circle.BorderThickness = active ? new Thickness(3.0) : new Thickness(1.5);
        }

        private SettingsVertical? _settingsWindow;

        private void OpenSettings()
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsVertical();

                // wenn User das X klickt: nicht "weg-disposen", sondern wieder referenz sauber machen
                _settingsWindow.Closed += (_, __) => _settingsWindow = null;
            }

            // Owner setzen (wichtig für Stacking auf Linux)
            _settingsWindow.Show(this);

            // Fokus nach vorne holen
            _settingsWindow.Activate();

            // Linux-Workaround: kurz Topmost toggeln, damit es wirklich über dem MainWindow landet
            _settingsWindow.Topmost = true;
            _settingsWindow.Topmost = false;
        }

        public void DisplayInnenruftasterquittung()
        {
            int index = ViewModel.InnenruftasterquittungEtage;     // 1 = unterste
            int zustand = ViewModel.InnenruftasterquittungZustand; // 1 aktiv, 0 aus
            if (index <= 0) return;

            int label = FloorIndexToDisplayLabel(index);
            if (_fixedInsideByLabel.TryGetValue(label, out var btn) && btn.Content is Border circle)
                SetButtonOutlineFixed(circle, HasAnyActiveCallAck(zustand));
        }




        private void Arrow_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is ValueTuple<int, ArrowDir> t)
            {
                int zielLabel = t.Item1;
                var dir = t.Item2;

                int calculatedEtage = DisplayLabelToFloorIndex(zielLabel);
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
                int calculatedEtage = DisplayLabelToFloorIndex(zielLabel);
                if (calculatedEtage < 1) return;
                byte floor = (byte)calculatedEtage;
                byte[] doors = GetInsideCallDoorBytes(calculatedEtage);

                foreach (byte door in doors)
                {
                    SerialPortManager.Instance.SendWithoutResponse(new byte[]
                    { 0x04, 0x01, 0x05, floor, 0x01, 0x00, door, 0x01 });
                }
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
                        case "Home":
                            WindowNavigationService.NavigateHome(this);
                            break;
                        case "Settings":
                            OpenSettings();
                            break;
                        /* case "Testrufe":
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
                            if (Terminal.Instance == null)
                            {
                                var terminal = new Terminal();
                                terminal.Show();
                                TerminalManager.terminalActive = true; // optional, Closed setzt wieder false
                            }
                            else
                            {
                                Terminal.Instance.Close(); // löst Closed aus, setzt Instance = null
                            }
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
