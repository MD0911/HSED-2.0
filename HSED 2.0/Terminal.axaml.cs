using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HSED_2._0;
using System;
using System.Diagnostics;
using Avalonia.Layout;

namespace HSED_2_0
{
    public partial class Terminal : Window
    {
        public static Terminal Instance { get; private set; }

        private const int Rows = 4;
        private const int MaxCols = 35;



        private readonly TerminalManager terminalManager = new TerminalManager();
        Bitmap minimieren = new Bitmap("Images/Icons/minimieren.png");
        Bitmap maximieren = new Bitmap("Images/Icons/maximieren.png");
        // ========== Fensterpositionen ==========
        // Normale Startposition (nicht gezoomt)
        private const int NORMAL_SPAWN_X = 340;
        private const int NORMAL_SPAWN_Y = 250;

        // Fensterposition im Zoom-Zustand
        private const int ZOOM_SPAWN_X = 0;
        private const int ZOOM_SPAWN_Y = -40;

        // ========== Canvas-Margins ==========
        // Canvas-Margin im Normalzustand (muss zur XAML passen!)
        private const int NORMAL_MARGIN_LEFT = -370;
        private const int NORMAL_MARGIN_TOP = -270;

        // Canvas-Margin im Zoomzustand
        private const int ZOOM_MARGIN_LEFT = 320;  // hier spielen, bis es gut aussieht
        private const int ZOOM_MARGIN_TOP = -30;

        private bool _isZoomed = false;

        // Wir merken uns die Position beim Start,
        // falls du sie später noch brauchst
        private PixelPoint _originalPosition;

        private const double ZoomFactor = 1.4;

        // Klasse: Terminal
        private readonly Grid[,] _cellContainers = new Grid[Rows, MaxCols];
        private readonly Image[,] _cellImages = new Image[Rows, MaxCols];
        private readonly Image[,] _cursorImages = new Image[Rows, MaxCols];
        private bool _uiCacheInitialized = false;

        public Terminal()
        {
            InitializeComponent();

            FensterSizeButton.Source = maximieren;

            // Cache direkt einmalig aufbauen (nach InitializeComponent!)
            BuildUiCache();
            ImageGrid.SizeChanged += (_, __) => UpdateColumnSeparator();

            this.Position = new PixelPoint(NORMAL_SPAWN_X, NORMAL_SPAWN_Y);
            _originalPosition = this.Position;

            SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x1B });

            terminalManager.Start();
            TerminalManager.RequestColumnsForCurrentTerminalMode(zoomEnabled: false);
            Instance = this;

            this.Closed += (_, __) =>
            {
                TerminalManager.terminalActive = false;

                terminalManager.Stop();
                Instance = null;

                // Cache resetten, weil Window neu geöffnet wird
                _uiCacheInitialized = false;

                // Optional: Referenzen freigeben (sauber für GC)
                for (int r = 0; r < Rows; r++)
                {
                for (int c = 0; c < MaxCols; c++)
                {
                    _cellContainers[r, c] = null;
                    _cellImages[r, c] = null;
                    _cursorImages[r, c] = null;
                }
            }

                // MainWindow sofort aktualisieren
                MainWindow.Instance?.ForceRefreshOverlaidUi();
            };


        }



        // Klasse: Terminal


        // Klasse: Terminal
        private void BuildUiCache()
        {
            if (_uiCacheInitialized)
                return;

            ImageGrid.Children.Clear();

            for (int row = 1; row <= Rows; row++)
            {
                for (int col = 1; col <= MaxCols; col++)
                {
                    var host = new Grid
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };

                    var cellImage = new Image { Stretch = Stretch.Uniform };
                    var cursorImage = new Image { Stretch = Stretch.Uniform };

                    host.Children.Add(cellImage);
                    host.Children.Add(cursorImage);
                    ImageGrid.Children.Add(host);

                    _cellContainers[row - 1, col - 1] = host;
                    _cellImages[row - 1, col - 1] = cellImage;
                    _cursorImages[row - 1, col - 1] = cursorImage;
                }
            }

            _uiCacheInitialized = true;
            SetColumnCount(TerminalManager.CurrentColumns);
        }

        // Klasse: Terminal
        public void UpdateCusorImage(int row, int col, Bitmap? bmp)
        {
            BuildUiCache();

            if (row < 1 || row > Rows || col < 1 || col > MaxCols)
                return;

            var img = _cursorImages[row - 1, col - 1];
            if (img == null) return;

            img.Source = bmp;
        }


        // Klasse: Terminal
        public void UpdateCellImage(int row, int col, Bitmap bmp)
        {
            BuildUiCache();

            if (row < 1 || row > Rows || col < 1 || col > MaxCols)
                return;

            var img = _cellImages[row - 1, col - 1];
            if (img == null)
                return;

            img.Source = bmp;
        }

        public void SetColumnCount(int columns)
        {
            BuildUiCache();

            int normalizedColumns = columns switch
            {
                28 => 28,
                26 => 26,
                35 => 35,
                _ => 16
            };

            ImageGrid.Columns = normalizedColumns;

            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < MaxCols; col++)
                {
                    bool visible = col < normalizedColumns;
                    if (_cellContainers[row, col] != null)
                        _cellContainers[row, col].IsVisible = visible;
                }
            }

            UpdateColumnSeparator();
        }

        public void ClearDisplay()
        {
            BuildUiCache();

            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < MaxCols; col++)
                {
                    if (_cellImages[row, col] != null)
                        _cellImages[row, col].Source = null;

                    if (_cursorImages[row, col] != null)
                        _cursorImages[row, col].Source = null;
                }
            }
        }

        private void UpdateColumnSeparator()
        {
            if (ColumnSeparatorOverlay == null || ImageGrid == null)
                return;

            int columns = TerminalManager.CurrentColumns;
            bool showSeparator = columns >= 28;
            ColumnSeparatorOverlay.IsVisible = showSeparator;

            if (!showSeparator)
                return;

            double totalWidth = ImageGrid.Bounds.Width;
            if (totalWidth <= 0)
                return;

            double x = ((totalWidth / columns) * 24.0) - 1.0;
            ColumnSeparatorOverlay.Margin = new Thickness(Math.Round(x), 0, 0, 0);
        }


        public void OnKeyButtonClick(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                string key = btn.Content?.ToString() ?? string.Empty;
                Debug.WriteLine($"Button {key} wurde geklickt.");

                byte code = key switch
                {
                    "1" => 0x31,
                    "2" => 0x32,
                    "3" => 0x33,
                    "4" => 0x34,
                    "5" => 0x35,
                    "6" => 0x36,
                    "7" => 0x37,
                    "8" => 0x38,
                    "9" => 0x39,
                    "0" => 0x30,
                    "ESC" => 0x1B,
                    "ENT" => 0x0D,
                    "↑" => 0x26,
                    "↓" => 0x28,
                    "→" => 0x3D,
                    "←" => 0x3C,
                    "F1" => 0x3A,
                    "F2" => 0x3B,
                    _ => (byte)0x00
                };

                if (code != 0x00)
                {
                    SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, code });
                }
            }
        }

        private void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Nur schließen – Rest erledigt das Closed-Event
            this.Close();
        }


        private void Button_Click_1(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (RootLayoutTransform == null || MainCanvas == null)
                return;

            if (!_isZoomed)
            {
                // Zoom aktivieren
                RootLayoutTransform.LayoutTransform = new TransformGroup
                {
                    Children =
                    {
                        new RotateTransform(90),
                        new ScaleTransform(ZoomFactor, ZoomFactor)
                    }
                };

                // Fenster an Zoom-Position
                this.Position = new PixelPoint(ZOOM_SPAWN_X, ZOOM_SPAWN_Y);

                // Canvas-Margin für Zoom
                MainCanvas.Margin = new Thickness(ZOOM_MARGIN_LEFT, ZOOM_MARGIN_TOP);

                FensterSizeButton.Width = 18;
                FensterSizeButton.Height = 18;
                FensterSizeButton.Source = minimieren;
                _isZoomed = true;
                TerminalManager.RequestColumnsForCurrentTerminalMode(zoomEnabled: true);
                
            }
            else
            {
                // Zoom deaktivieren
                RootLayoutTransform.LayoutTransform = new TransformGroup
                {
                    Children =
                    {
                        new RotateTransform(90),
                        new ScaleTransform(1.0, 1.0)
                    }
                };

                // Fenster auf "Normal-Spawn" (Konstante)
                this.Position = new PixelPoint(NORMAL_SPAWN_X, NORMAL_SPAWN_Y);

                // Canvas-Margin auf die Normal-Konstanten
                MainCanvas.Margin = new Thickness(NORMAL_MARGIN_LEFT, NORMAL_MARGIN_TOP);

                FensterSizeButton.Width= 14;
                FensterSizeButton.Height= 14;
                FensterSizeButton.Source = maximieren;
                _isZoomed = false;
                TerminalManager.RequestColumnsForCurrentTerminalMode(zoomEnabled: false);

            }
        }
    }
}
