using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using HSED_2_0;
using System.Threading;
using Avalonia.Rendering;
using System.Threading.Tasks;

namespace HSED_2._0
{
    public partial class Code : Window
    {
        bool NavBarStatus = false;
        private CancellationTokenSource _cancellationTokenSource;

        public Code()
        {
            InitializeComponent();
            this.Position = new Avalonia.PixelPoint(0, 0);
            
        }

        private void Button_Click_Numpad(object? sender, RoutedEventArgs e)
        {
            // Wenn der bisherige Text "Kommando" ist, wird er geleert.
            if (Input.Text == "Kommando")
            {
                Input.Text = "";
            }

            if (sender is Button button)
            {
                string buttonTag = button.Tag?.ToString();
                // Je nach Tag wird eine entsprechende Ziffer an den Input angehängt.
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

        private async void Button_Click_Action(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                string buttonTag = button.Tag?.ToString();

                if (buttonTag == "ESC")
                {
                    Input.Text = "Kommando";
                }
                if (buttonTag == "E")
                {
                    string inputText = Input.Text;
                    // Durchlaufe den Input-Text Zeichen für Zeichen und sende die Bytes direkt
                    foreach (char c in inputText)
                    {
                        if (char.IsDigit(c))
                        {
                            int asciiDecimal = (int)c;
                            byte asciiByte = (byte)asciiDecimal;
                            SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, asciiByte });
                            await Task.Delay(100);
                        }
                    }
                    SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x0D });
                    Input.Text = "Kommando";
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
