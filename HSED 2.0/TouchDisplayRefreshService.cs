using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace HSED_2._0;

internal static class TouchDisplayRefreshService
{
    public static async Task RequestRefreshAsync(Window owner)
    {
        try
        {
            var dialog = new RefreshConfirmationDialog();
            dialog.Show(owner);

            var confirmed = await dialog.WaitForChoiceAsync();
            if (!confirmed)
                return;

            await RequestRefreshWithoutConfirmationAsync(owner);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Soft-Refresh fehlgeschlagen: " + ex.Message);
        }
    }

    public static async Task RequestRefreshWithoutConfirmationAsync(Window owner)
    {
        try
        {
            WindowNavigationService.NavigateHome(owner);

            var mainWindow = MainWindow.Instance;
            if (mainWindow == null)
                return;

            await mainWindow.PerformSoftRefreshAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Soft-Refresh ohne Bestätigung fehlgeschlagen: " + ex.Message);
        }
    }
}
