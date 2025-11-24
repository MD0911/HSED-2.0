using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Diagnostics;

namespace HSED_2_0
{
    public partial class Terminal : Window
    {
        public static Terminal Instance { get; private set; }

        private readonly TerminalManager terminalManager = new TerminalManager();

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
        private const int NORMAL_MARGIN_TOP = -250;

        // Canvas-Margin im Zoomzustand
        private const int ZOOM_MARGIN_LEFT = 320;  // hier spielen, bis es gut aussieht
        private const int ZOOM_MARGIN_TOP = -30;

        private bool _isZoomed = false;

        // Wir merken uns die Position beim Start,
        // falls du sie später noch brauchst
        private PixelPoint _originalPosition;

        private const double ZoomFactor = 1.4;

        public Terminal()
        {
            InitializeComponent();

            // Fenster-Startposition festlegen
            this.Position = new PixelPoint(NORMAL_SPAWN_X, NORMAL_SPAWN_Y);
            _originalPosition = this.Position;

            // WICHTIG:
            // KEIN Setzen von MainCanvas.Margin hier!
            // => beim ersten Öffnen gelten die Werte aus der XAML (-390, -280, 0, 0)

            SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x1B });

            terminalManager.Start();
            Instance = this;
        }

        public void UpdateCellImage(int row, int col, Bitmap bmp)
        {
            string cellName = $"Cell_{row}_{col}";
            if (this.FindControl<Image>(cellName) is Image cell)
            {
                cell.Source = bmp;
            }
        }

        public void UpdateCusorImage(int row, int col, Bitmap bmp)
        {
            string cellName = $"Cursor_{row}_{col}";
            if (this.FindControl<Image>(cellName) is Image cursor)
            {
                cursor.Source = bmp;
            }
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
            TerminalManager.terminalActive = false;
            terminalManager.Stop();
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

                _isZoomed = true;
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

                _isZoomed = false;
            }
        }
    }
}
