using Avalonia.Controls;
using Avalonia.Interactivity;
using HSED_2_0;
using System.Threading.Tasks;

namespace HSED_2._0
{
    public partial class Code : Window
    {
        public Code()
        {
            InitializeComponent();
            // Fensterposition wie gewünscht
            this.Position = new Avalonia.PixelPoint(0, 0);
        }

        // Ziffern-Buttons (0–9)
        private void Button_Click_Numpad(object? sender, RoutedEventArgs e)
        {
            if (Input.Text == "Kommando")
                Input.Text = "";

            if (sender is Button button && button.Tag is string tag && int.TryParse(tag, out _))
            {
                // Tag ist "0"–"9" → direkt an Text anhängen
                Input.Text += tag;
            }

            // Kein erzwungener Render-Aufruf mehr! Avalonia aktualisiert das TextBlock automatisch.
        }

        // ESC und E
        private void Button_Click_Action(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag)
            {
                if (tag == "ESC")
                {
                    Input.Text = "Kommando";
                }
                else if (tag == "E")
                {
                    string toSend = Input.Text;
                    Input.Text = "Kommando";

                    // Serielle Telegramme asynchron senden
                    _ = Task.Run(async () =>
                    {
                        foreach (char c in toSend)
                        {
                            if (char.IsDigit(c))
                            {
                                byte asciiByte = (byte)c;
                                SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, asciiByte });
                                // anstatt 100 ms nehmen wir 1 ms: extrem knapp, blockiert kaum
                                await Task.Delay(1);
                            }
                        }
                        // Terminator-Byte
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x0D });
                    });
                }
            }
        }

        // Close-Button oben rechts
        private void Button_Click(object? sender, RoutedEventArgs e)
        {
            TerminalManager.terminalActive = false;
            this.Close();
        }
    }
}
