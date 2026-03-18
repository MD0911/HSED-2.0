using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using HSED_2._0.Models;
using HSED_2._0.ViewModels;

namespace HSED_2._0;

public partial class WifiWindow : Window
{
    private bool _opening;
    private bool _navBarOpen = false;
    private readonly DispatcherTimer _spinnerTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private Ellipse? _scanSpinner;
    private WifiViewModel? _viewModel;
    private double _spinnerAngle;

    public WifiWindow()
    {
        InitializeComponent();
        Position = new PixelPoint(0, 0);
        DataContext = new WifiViewModel();
        _viewModel = DataContext as WifiViewModel;
        _scanSpinner = this.FindControl<Ellipse>("ScanSpinner");
        _spinnerTimer.Tick += SpinnerTimer_Tick;

        // Falls du nicht schon im XAML "SelectionChanged" verdrahtet hast,
        // kannst du es hier sicher tun. (Schadet nicht, wenn XAML es bereits hat.)
        var list = this.FindControl<ListBox>("WifiList");
        if (list != null)
            list.SelectionChanged += WifiList_SelectionChanged;

        if (_viewModel != null)
            _viewModel.PropertyChanged += WifiViewModel_PropertyChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is WifiViewModel vm)
            vm.ScanCommand.Execute(null);
    }

    protected override void OnClosed(EventArgs e)
    {
        _spinnerTimer.Stop();

        if (_viewModel != null)
            _viewModel.PropertyChanged -= WifiViewModel_PropertyChanged;

        base.OnClosed(e);
    }

    private void WifiViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WifiViewModel.IsScanning) && sender is WifiViewModel vm)
            UpdateSpinnerState(vm.IsScanning);
    }

    private void UpdateSpinnerState(bool isScanning)
    {
        if (_scanSpinner == null)
            return;

        if (isScanning)
        {
            _spinnerAngle = 0;
            ApplySpinnerRotation();
            _spinnerTimer.Start();
            return;
        }

        _spinnerTimer.Stop();
        _spinnerAngle = 0;
        ApplySpinnerRotation();
    }

    private void SpinnerTimer_Tick(object? sender, EventArgs e)
    {
        _spinnerAngle = (_spinnerAngle + 24) % 360;
        ApplySpinnerRotation();
    }

    private void ApplySpinnerRotation()
    {
        if (_scanSpinner?.RenderTransform is RotateTransform rotateTransform)
            rotateTransform.Angle = _spinnerAngle;
    }

    private async void WifiList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_opening)
            return;

        if (DataContext is not WifiViewModel vm)
            return;

        // Avalonia setzt SelectedItem manchmal erst nach dem Event.
        // Deswegen nehmen wir das Item direkt aus dem Event, falls vorhanden.
        WifiNetworkItem? selectedFromEvent = null;
        if (e.AddedItems is { Count: > 0 })
            selectedFromEvent = e.AddedItems[0] as WifiNetworkItem;

        var selected = selectedFromEvent ?? vm.SelectedNetwork;
        if (selected == null)
            return;

        // sicherstellen, dass VM auch das SelectedNetwork bekommt
        vm.SelectedNetwork = selected;

        _opening = true;

        try
        {
            vm.ClearPassword();

            var pwdWin = new WifiPasswordWindow
            {
                DataContext = vm
            };

            await pwdWin.ShowDialog(this);
        }
        finally
        {
            // Wichtig: zurÃ¼cksetzen, damit man wieder klicken kann
            vm.SelectedNetwork = null;

            // ZusÃ¤tzlich ListBox Selection leeren, sonst feuert es manchmal nicht erneut
            if (sender is ListBox lb)
                lb.SelectedItem = null;

            _opening = false;
        }
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

                // weitere Cases kannst du spÃ¤ter wieder aktivieren
        }
    }

    private void ToggleNavBar()
    {
        if (!_navBarOpen)
        {
            NavBar.Width = 160; // statt +=100, damit es nie â€ždriftetâ€œ
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
            NavBar.Width = 60; // statt -=100, damit es nie â€ždriftetâ€œ
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
}
