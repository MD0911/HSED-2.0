using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HSED_2_0;
using System;
using System.Threading.Tasks;

namespace HSED_2._0
{
    public partial class Code : Window
    {
        public Code()
        {
            InitializeComponent();
            // Setze Fensterposition wie gewünscht
            this.Position = new Avalonia.PixelPoint(0, 0);
        }

        // Digit-Buttons (0–9): Nur Textfeld aktualisieren und sofortiges Rendern anstoßen
        private void Button_Click_Numpad(object? sender, RoutedEventArgs e)
        {
            // Wenn das Feld noch "Kommando" zeigt, leeren
            if (Input.Text == "Kommando")
                Input.Text = "";

            if (sender is Button button && button.Tag is string tag && int.TryParse(tag, out _))
            {
                // Tag ist z.B. "0" bis "9" → direkt an Text anhängen
                Input.Text += tag;
            }

            // Avalonia sofort neu rendern lassen, damit man den eingegebenen Buchstaben 
            // ohne Verzögerung sieht.
            Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        }

        // Action-Buttons: ESC und E
        private void Button_Click_Action(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag)
            {
                if (tag == "ESC")
                {
                    // Eingabe zurücksetzen und sofort rendern
                    Input.Text = "Kommando";
                    Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                }
                else if (tag == "E")
                {
                    // Eingabetext zwischenspeichern
                    string inputText = Input.Text;

                    // UI sofort zurücksetzen (ohne auf serielle Sendevorgänge zu warten)
                    Input.Text = "Kommando";
                    Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

                    // Serielles Senden im Hintergrund (Task.Run), damit UI nicht blockiert
                    Task.Run(async () =>
                    {
                        foreach (char c in inputText)
                        {
                            if (char.IsDigit(c))
                            {
                                byte asciiByte = (byte)c;
                                SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, asciiByte });
                                // Wenn das Gerät wirklich eine kleine Pause pro Zeichen braucht,
                                // kann man hier z.B. 10 ms statt 100 ms verwenden. 
                                await Task.Delay(10);
                            }
                        }
                        // Terminator-Byte senden
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x0D });
                    });
                }
            }
        }

        // Close-Button oben rechts: Fenster schließen und Terminal-Flag zurücksetzen
        private void Button_Click(object? sender, RoutedEventArgs e)
        {
            TerminalManager.terminalActive = false;
            this.Close();
        }
    }
}
