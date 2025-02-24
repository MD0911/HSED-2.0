using Avalonia.Controls;
using System;
using System.IO.Ports;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading;
using System.Diagnostics;
namespace HSED_2._0
{

    public partial class Settings : Window
    {
        bool NavBarStatus = false;

        public Settings()
        {
            InitializeComponent();

        }

        private void Button_Click_Settings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
                            //var newWindowHome = new MainWindow();
                           // newWindowHome.Show();
                            this.Close();
                            break;
                        case "Codes":
                            var newWindowCode = new Code();
                            newWindowCode.Show();
                            break;
                        case "Testrufe":
                            var newWindowTestrufe = new Testrufe();
                            newWindowTestrufe.Show();
                            this.Close();
                            break;


                    }

                }
            }
        }
    }
}