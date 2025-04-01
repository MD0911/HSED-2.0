using Avalonia.Controls;
using Avalonia.Media.Imaging;
using System;
using System.Diagnostics;

namespace HSED_2_0
{
    public partial class Terminal : Window
    {
        public static Terminal Instance { get; private set; }
        TerminalManager terminalManager = new TerminalManager();

        public Terminal()
        {
            InitializeComponent();
            // Fensterposition über die Property 'Position' setzen
            this.Position = new Avalonia.PixelPoint(0, 0);
            terminalManager.Start();
            Instance = this;
        }


        /// <summary>
        /// Aktualisiert eine Zelle im Bild-Display.
        /// </summary>
        public void UpdateCellImage(int row, int col, Bitmap bmp)
        {
            string cellName = $"Cell_{row}_{col}";
            if (this.FindControl<Image>(cellName) is Image cell)
            {
                cell.Source = bmp;
            }
        }

        /// <summary>
        /// Aktualisiert das Cursor-Overlay in der angegebenen Zelle.
        /// </summary>
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
        string key = btn.Content.ToString();
        Debug.WriteLine($"Button {key} wurde geklickt.");

        switch (key)
        {
            case "1":
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x31 });
                        break;
            case "2":
                        // Logik für Button 2
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x32 });
                        break;
            case "3":
                        // Logik für Button 3
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x33});
                        break;
            case "4":
                        // Logik für Button 4
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x34 });
                        break;
            case "5":
                        // Logik für Button 5
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x35 });
                        break;
            case "6":
                        // Logik für Button 6
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x36 });
                        break;
            case "7":
                        // Logik für Button 7
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x37 });
                        break;
            case "8":
                        // Logik für Button 8
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x38 });
                        break;
            case "9":
                        // Logik für Button 9
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x39 });
                        break;
            case "0":
                        // Logik für Button 0
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x30 });
                        break;
            case "ESC":
                        // Logik für ESC-Taste
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x1B });
                        break;
            case "↵":
                        // Logik für ENTER-Taste
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x0D });
                        break;
             case "↑":
                        // Logik für ESC-Taste
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x26 });
                        break;
             case "↓":
                        // Logik für ENTER-Taste
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x28 });
                        break;

                    default:
                // Fallback für unbekannte Buttons
                break;
        }
    }
}

        private void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            TerminalManager.terminalActive = false;
            this.Close();
        }
    }

    }

