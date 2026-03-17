using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using HSED_2_0;

namespace HSED_2._0;

public partial class SettingsVertical : Window
{
    bool NavBarStatus = false;
    private InfoWindow? _infoWindow;
    private WifiWindow? _wifiWindow;

    private Updater? _updateWindow;
    private UpdaterUSB? _updateUsbWindow;



    public SettingsVertical()
    {
        InitializeComponent();
        Position = new PixelPoint(0, 0);

        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }


    public void CloseSettings()
    {
        Hide(); // oder base.Close()
    }


    private async void Button_Click_Settings(object? sender, RoutedEventArgs e)

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
                    case "Settings":
                        this.Hide();
                        break;
                    case "Menu":
                        this.Hide();
                        break;
                    case "Info":
                        if (_infoWindow == null)
                        {
                            _infoWindow = new InfoWindow();
                            _infoWindow.Closed += (_, __) => _infoWindow = null;
                        }

                        _infoWindow.Show(this);
                        _infoWindow.Activate();
                        _infoWindow.Topmost = true;
                        _infoWindow.Topmost = false;
                        break;
                    case "Wifi":
                        if (_wifiWindow == null)
                        {
                            _wifiWindow = new WifiWindow();
                            _wifiWindow.Closed += (_, __) => _wifiWindow = null;
                        }

                        _wifiWindow.Show(this);
                        _wifiWindow.Activate();
                        _wifiWindow.Topmost = true;
                        _wifiWindow.Topmost = false;
                        break;

                    case "Update":
                        {
                            var dialog = new UpdateChoiceDialog();
                            dialog.Show(this);

                            var mode = await dialog.WaitForChoiceAsync();

                            if (mode == UpdateMode.Online)
                            {
                                if (_updateWindow == null)
                                {
                                    _updateWindow = new Updater();
                                    _updateWindow.Closed += (_, __) => _updateWindow = null;
                                }

                                _updateWindow.Show(this);
                                _updateWindow.Activate();
                                _updateWindow.Topmost = true;
                                _updateWindow.Topmost = false;

                                _updateWindow.StartUpdater();
                            }
                            else if (mode == UpdateMode.OfflineUsb)
                            {
                                if (_updateUsbWindow == null)
                                {
                                    _updateUsbWindow = new UpdaterUSB();
                                    _updateUsbWindow.Closed += (_, __) => _updateUsbWindow = null;
                                }

                                _updateUsbWindow.Show(this);
                                _updateUsbWindow.Activate();
                                _updateUsbWindow.Topmost = true;
                                _updateUsbWindow.Topmost = false;

                                // Falls du dort auch einen Start brauchst
                                // _updateUsbWindow.StartUpdaterUsb();
                            }

                            break;
                        }


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

    private void Button_Click(object? sender, RoutedEventArgs e)
    {
    }
}
