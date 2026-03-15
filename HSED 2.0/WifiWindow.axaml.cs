using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using HSED_2._0.Models;
using HSED_2._0.ViewModels;

namespace HSED_2._0;

public partial class WifiWindow : Window
{
    private bool _opening;
    private bool _navBarOpen = false;

    public WifiWindow()
    {
        InitializeComponent();
        Position = new PixelPoint(0, 0);
        DataContext = new WifiViewModel();

        // Falls du nicht schon im XAML "SelectionChanged" verdrahtet hast,
        // kannst du es hier sicher tun. (Schadet nicht, wenn XAML es bereits hat.)
        var list = this.FindControl<ListBox>("WifiList");
        if (list != null)
            list.SelectionChanged += WifiList_SelectionChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
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
            // Wichtig: zurücksetzen, damit man wieder klicken kann
            vm.SelectedNetwork = null;

            // Zusätzlich ListBox Selection leeren, sonst feuert es manchmal nicht erneut
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
            case "Settings":
            case "Menu":
                (Owner as Window)?.Hide();
                Hide();
                break;

                // weitere Cases kannst du später wieder aktivieren
        }
    }

    private void ToggleNavBar()
    {
        if (!_navBarOpen)
        {
            NavBar.Width = 160; // statt +=100, damit es nie „driftet“
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
            NavBar.Width = 60; // statt -=100, damit es nie „driftet“
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
