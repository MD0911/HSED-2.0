using Avalonia.Controls;
using System;
using System.IO.Ports;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading;
using System.Diagnostics;
using HSED_2_0;
using System.Text;
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
          

        }

       

        private void HseUpdated()
        {
            
        }

            /* private void Button_Click_Settings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
             {
                 if (sender is Button button)
                 {
                     // Speichere den Namen des Buttons als String
                     string buttonTag = button.Tag?.ToString();
                     if (buttonTag == "Menu")
                     {


                         if (NavBarStatus == false)
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


                             case "Home":
                                 var newWindowHome = new MainWindow();
                                 newWindowHome.Show();
                                 this.Close();
                                 break;


                         }

                     }
                 }
             } */
            private void Border_PointerPressed_1(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (Input.Text == "Kommando")
            {
                Input.Text = "";
            }

            if (sender is Border border)
            {
                string buttonTag = border.Tag?.ToString();

                switch (buttonTag)
                {

                    case "0":
                        Input.Text = Input.Text + "0";
                        break;
                    case "1":
                        Input.Text = Input.Text + "1";
                        break;
                    case "2":
                        Input.Text = Input.Text + "2";
                        break;
                    case "3":
                        Input.Text = Input.Text + "3";
                        break;
                    case "4":
                        Input.Text = Input.Text + "4";
                        break;
                    case "5":
                        Input.Text = Input.Text + "5";
                        break;
                    case "6":
                        Input.Text = Input.Text + "6";
                        break;
                    case "7":
                        Input.Text = Input.Text + "7";
                        break;
                    case "8":
                        Input.Text = Input.Text + "8";
                        break;
                    case "9":
                        Input.Text = Input.Text + "9";
                        break;

                }
            }
        }

        private void Border_PointerPressed_2(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {

            if (sender is Border border)
            {
                string buttonTag = border.Tag?.ToString();

                if(buttonTag == "ESC")
                {
                    Input.Text = "Kommando";
                }
                if (buttonTag == "E")
                {
                    int input = Convert.ToInt32(Input.Text);
                    Input.Text = "Kommando";

                    /*   switch (input) {
                           case 15:
                               HseCom.SendHseCommand(new byte[] {0x07, 0x04, 0x01});
                               break;


                       }*/
                }


            }
            }

        
        }
}