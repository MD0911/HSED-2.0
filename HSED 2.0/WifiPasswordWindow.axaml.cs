using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HSED_2._0.ViewModels;

namespace HSED_2._0;

public partial class WifiPasswordWindow : Window
{
    bool NavBarStatus = false;

    public WifiPasswordWindow()
    {
        InitializeComponent();

        var passwordBox = this.FindControl<TextBox>("PasswordBox");

        if (passwordBox != null)
            InputMethod.SetIsInputMethodEnabled(passwordBox, false);

        Opened += (_, __) =>
        {
            if (DataContext is WifiViewModel vm)
            {
                // Keyboard dauerhaft sichtbar: egal ob ViewModel Binding benutzt wird oder nicht
                vm.IsKeyboardVisible = true;
                vm.StatusText = "";
            }

            // Autofocus, damit man sofort tippen kann
            passwordBox?.Focus();
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // Optional: bleibt drin, schadet nicht (Keyboard ist sowieso immer sichtbar)
    private void PasswordBox_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is WifiViewModel vm)
            vm.IsKeyboardVisible = true;
    }

    private async void Connect_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WifiViewModel vm)
            return;

        if (string.IsNullOrWhiteSpace(vm.WifiPassword))
        {
            vm.StatusText = "Bitte Passwort eingeben";
            vm.IsKeyboardVisible = true;
            return;
        }

        try
        {
            vm.StatusText = "Verbinde...";
            await vm.ConnectNowAsync();

            // Erfolg -> Fenster schließen
            Close();
        }
        catch
        {
            // Fehlertext steht bereits in StatusText
            // Fenster bleibt offen
            vm.IsKeyboardVisible = true;
        }
    }

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
                    case "Back":
                        Close();
                        break;
                    case "Home":
                        Close();
                        Dispatcher.UIThread.Post(() => WindowNavigationService.NavigateHome());
                        break;
                    case "SelfDia":
                        _ = TouchDisplayRefreshService.RequestRefreshAsync(this);
                        break;

                        /*
                        case "Testrufe":
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
                            break;
                        */
                }
            }
        }
    }
}
