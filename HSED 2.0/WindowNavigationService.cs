using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace HSED_2._0;

internal static class WindowNavigationService
{
    public static void ReturnToSettings(Window currentWindow)
    {
        if (currentWindow.Owner is Window owner)
        {
            owner.Show();
            owner.Activate();
            owner.Topmost = true;
            owner.Topmost = false;
        }

        currentWindow.Hide();
    }

    public static void NavigateHome(Window? currentWindow = null)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var mainWindow = MainWindow.Instance;
        if (mainWindow == null || !desktop.Windows.Contains(mainWindow))
        {
            mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
        }

        foreach (var window in desktop.Windows.ToList())
        {
            if (ReferenceEquals(window, mainWindow))
                continue;

            try
            {
                window.Hide();
            }
            catch (InvalidOperationException)
            {
                // Ignorieren: einzelne Fenster können bereits im Schließen sein.
            }
        }

        if (!mainWindow.IsVisible)
            mainWindow.Show();

        mainWindow.Activate();
        mainWindow.Topmost = true;
        mainWindow.Topmost = false;

        if (currentWindow != null && !ReferenceEquals(currentWindow, mainWindow))
            currentWindow.Hide();
    }
}
