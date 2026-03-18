using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HSED_2._0.Controls;

namespace HSED_2._0;

public partial class SerialSettingsWindow : Window
{
    private static readonly string[] CommonBaudrates =
    {
        "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200"
    };

    private bool _navBarOpen;
    private TextBox? _serialPortTextBox;
    private Button? _applyButton;
    private Border? _baudrateDropdown;
    private Border? _keyboardHost;
    private Border? _connectionErrorOverlay;
    private TextBlock? _selectedBaudrateTextBlock;
    private OnScreenKeyboard? _keyboardControl;
    private string? _selectedBaudrate;
    private readonly bool _showConnectionErrorOnOpen;

    public SerialSettingsWindow() : this(false)
    {
    }

    public SerialSettingsWindow(bool showConnectionErrorOnOpen)
    {
        _showConnectionErrorOnOpen = showConnectionErrorOnOpen;
        InitializeComponent();
        Position = new PixelPoint(0, 0);

        _serialPortTextBox = this.FindControl<TextBox>("SerialPortTextBox");
        _applyButton = this.FindControl<Button>("ApplyButton");
        _baudrateDropdown = this.FindControl<Border>("BaudrateDropdown");
        _keyboardHost = this.FindControl<Border>("KeyboardHost");
        _connectionErrorOverlay = this.FindControl<Border>("ConnectionErrorOverlay");
        _selectedBaudrateTextBlock = this.FindControl<TextBlock>("SelectedBaudrateTextBlock");
        _keyboardControl = this.FindControl<OnScreenKeyboard>("KeyboardControl");

        ConfigureBaudrateOptions();
        LoadConfig();

        if (_keyboardControl != null)
            _keyboardControl.TargetTextBox = _serialPortTextBox;

        SetKeyboardVisible(false);
        SetBaudrateDropdownVisible(false);

        if (_showConnectionErrorOnOpen)
            Dispatcher.UIThread.Post(ShowConnectionErrorPopup, DispatcherPriority.Loaded);
    }

    private string ConfigPath => Path.Combine(AppContext.BaseDirectory, "config.json");

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void ConfigureBaudrateOptions()
    {
        _selectedBaudrate ??= CommonBaudrates[0];
        UpdateSelectedBaudrateText();
    }

    private void LoadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return;

            var config = JsonSerializer.Deserialize<SerialConfigRoot>(
                File.ReadAllText(ConfigPath),
                JsonOptions());

            if (config?.SerialSettings == null)
                return;

            if (_serialPortTextBox != null)
                _serialPortTextBox.Text = config.SerialSettings.SerialPort ?? string.Empty;

            SetSelectedBaudrate(config.SerialSettings.SerialBaudrate);
        }
        catch
        {
            // Fenster bleibt benutzbar.
        }
    }

    private void SetSelectedBaudrate(string? baudrate)
    {
        if (string.IsNullOrWhiteSpace(baudrate))
            return;

        _selectedBaudrate = baudrate;
        UpdateSelectedBaudrateText();
    }

    private async void Apply_Click(object? sender, RoutedEventArgs e)
    {
        var serialPort = _serialPortTextBox?.Text?.Trim() ?? string.Empty;
        var baudrateText = _selectedBaudrate?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(serialPort))
            return;

        if (!int.TryParse(baudrateText, out var baudrate) || baudrate <= 0)
            return;

        try
        {
            var config = new SerialConfigRoot
            {
                SerialSettings = new SerialSettingsConfig
                {
                    SerialPort = serialPort,
                    SerialBaudrate = baudrate.ToString()
                }
            };

            var json = JsonSerializer.Serialize(config, JsonOptionsIndented());
            File.WriteAllText(ConfigPath, json);

            if (_applyButton != null)
                _applyButton.IsEnabled = false;

            await Dispatcher.UIThread.InvokeAsync(RestartApplication, DispatcherPriority.Background);
        }
        catch
        {
            if (_applyButton != null)
                _applyButton.IsEnabled = true;
        }
    }

    private void SerialPortTextBox_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ShowKeyboardForPortInput();
    }

    private void SerialPortTextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        ShowKeyboardForPortInput();
    }

    private void BaudrateButton_Click(object? sender, RoutedEventArgs e)
    {
        var newState = !(_baudrateDropdown?.IsVisible ?? false);
        SetKeyboardVisible(false);
        SetBaudrateDropdownVisible(newState);
    }

    private void BaudrateOption_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        _selectedBaudrate = button.Content?.ToString();
        UpdateSelectedBaudrateText();
        SetBaudrateDropdownVisible(false);
        SetKeyboardVisible(false);
    }

    private void UpdateSelectedBaudrateText()
    {
        if (_selectedBaudrateTextBlock != null)
            _selectedBaudrateTextBlock.Text = string.IsNullOrWhiteSpace(_selectedBaudrate)
                ? "Baudrate w\u00e4hlen"
                : _selectedBaudrate;
    }

    private void SetKeyboardVisible(bool isVisible)
    {
        if (_keyboardHost != null)
            _keyboardHost.IsVisible = isVisible;
    }

    private void SetBaudrateDropdownVisible(bool isVisible)
    {
        if (_baudrateDropdown != null)
            _baudrateDropdown.IsVisible = isVisible;
    }

    private void ShowKeyboardForPortInput()
    {
        if (_keyboardControl != null)
            _keyboardControl.TargetTextBox = _serialPortTextBox;

        SetBaudrateDropdownVisible(false);
        SetKeyboardVisible(true);
    }

    public void ShowConnectionErrorPopup()
    {
        SetKeyboardVisible(false);
        SetBaudrateDropdownVisible(false);

        if (_connectionErrorOverlay != null)
            _connectionErrorOverlay.IsVisible = true;
    }

    public void HideConnectionErrorPopup()
    {
        if (_connectionErrorOverlay != null)
            _connectionErrorOverlay.IsVisible = false;
    }

    private void ConnectionErrorPopupOk_Click(object? sender, RoutedEventArgs e)
    {
        HideConnectionErrorPopup();
    }

    private void Button_Click_Settings(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var buttonTag = button.Tag?.ToString() ?? string.Empty;

        if (buttonTag == "Menu")
        {
            ToggleNavBar();
            return;
        }

        // Im Start-Fehlermodus darf die Seitennavigation das Fenster nicht verstecken,
        // weil noch kein benutzbares MainWindow bereitsteht.
        if (_showConnectionErrorOnOpen)
            return;

        switch (buttonTag)
        {
            case "Back":
                WindowNavigationService.ReturnToSettings(this);
                break;
            case "Home":
                WindowNavigationService.NavigateHome(this);
                break;
            case "SelfDia":
                _ = TouchDisplayRefreshService.RequestRefreshAsync(this);
                break;
        }
    }

    private void ToggleNavBar()
    {
        if (!_navBarOpen)
        {
            NavBar.Width = 160;
            StackPanelNavBar.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            StackPanelNavBar.Margin = new Thickness(10, 25, 0, 0);
            SettingsText.IsVisible = true;
            ButtonSettings.Width = 100;
            SettingsText2.IsVisible = true;
            ButtonSettings2.Width = 100;
            SettingsText3.IsVisible = true;
            ButtonSettings3.Width = 100;
            SettingsText4.IsVisible = true;
            ButtonSettings4.Width = 100;
            SettingsText6.IsVisible = true;
            ButtonSettings6.Width = 100;
            SettingsText7.IsVisible = true;
            ButtonSettings7.Width = 100;
            Overlap.IsVisible = true;
            _navBarOpen = true;
        }
        else
        {
            NavBar.Width = 60;
            StackPanelNavBar.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            StackPanelNavBar.Margin = new Thickness(0, 25, 0, 0);
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
            _navBarOpen = false;
        }
    }

    private void RestartApplication()
    {
        try
        {
            try
            {
                SerialPortManager.Instance.Close();
            }
            catch
            {
            }

            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new InvalidOperationException("Der Anwendungspfad konnte nicht ermittelt werden.");

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            };

            Process.Start(startInfo);
            Close();

            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
        catch
        {
            if (_applyButton != null)
                _applyButton.IsEnabled = true;
        }
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    private static JsonSerializerOptions JsonOptionsIndented()
    {
        return new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };
    }

    private sealed class SerialConfigRoot
    {
        public SerialSettingsConfig? SerialSettings { get; set; }
    }

    private sealed class SerialSettingsConfig
    {
        public string? SerialPort { get; set; }
        public string? SerialBaudrate { get; set; }
    }
}
