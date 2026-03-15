using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace HSED_2._0;

public partial class UpdaterUSB : Window
{
    private bool NavBarStatus = false;
    private bool _updateRunning = false;

    public UpdaterUSB()
    {
        InitializeComponent();
        Position = new PixelPoint(0, 0);

        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };

        // Optional: Starttexte
        UsbStateText.Text = "Bereit";
        UsbDetailText.Text = "Drücke auf Update starten. Das Script prüft USB und LinuxArm.rar.";
        StatusText.Text = "";
        UpdateProgress.Value = 0;
        UpdateProgress.IsIndeterminate = false;
    }

    public void StartUpdaterUSB()
    {
        // Nichts mehr nötig, Button ist immer aktiv
    }

    private async void UpdateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_updateRunning) return;

        _updateRunning = true;
        UpdateButton.IsEnabled = false;

        SetProgress(0);
        SetProgressIndeterminate(true);

        try
        {
            UsbStateText.Text = "Update läuft";
            UsbDetailText.Text = "USB wird geprüft und ggf. gemountet...";

            Status("Starte USB Update. Log: /tmp/hsed_usb_update.log");

            var scriptPath = ResolveUsbUpdateScriptPath();
            if (!File.Exists(scriptPath))
            {
                Status("FEHLER: Script nicht gefunden: " + scriptPath);
                UsbStateText.Text = "Fehler";
                UsbDetailText.Text = "Script fehlt";
                return;
            }

            // Script ausführbar machen
            var chmodExit = await RunProcessAsync("/bin/chmod", $"+x \"{scriptPath}\"");
            if (chmodExit != 0)
            {
                Status($"FEHLER: chmod fehlgeschlagen (ExitCode {chmodExit}).");
                UsbStateText.Text = "Fehler";
                UsbDetailText.Text = "chmod fehlgeschlagen";
                return;
            }

            // Script starten
            var exit = await RunProcessAsync("/bin/bash", $"\"{scriptPath}\"");

            // Wenn das Gerät rebootet, siehst du das nicht mehr.
            // Wenn kein reboot: hier Status anzeigen.
            if (exit == 0)
            {
                UsbStateText.Text = "Fertig";
                UsbDetailText.Text = "Script beendet. Falls kein Reboot: bitte Log prüfen.";
                Status("USB Update erfolgreich beendet. Log: /tmp/hsed_usb_update.log");
            }
            else
            {
                UsbStateText.Text = "Fehler";
                UsbDetailText.Text = $"Script ExitCode: {exit} (Details im Log)";
                Status($"USB Update fehlgeschlagen (ExitCode {exit}). Log: /tmp/hsed_usb_update.log");
            }
        }
        catch (Exception ex)
        {
            UsbStateText.Text = "Fehler";
            UsbDetailText.Text = "Exception";
            Status("Fehler beim USB Update: " + ex.Message);
        }
        finally
        {
            SetProgressIndeterminate(false);
            SetProgress(100);

            _updateRunning = false;
            UpdateButton.IsEnabled = true;
        }
    }

    private string ResolveUsbUpdateScriptPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "LinuxScripts", "usb_update_hsed.sh");
    }

    private async Task<int> RunProcessAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var p = new Process { StartInfo = psi };
        p.Start();

        var stdOutTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await p.StandardOutput.ReadLineAsync()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    Status(line.Trim());
                    TryParseAndSetProgress(line);
                }
            }
        });

        var stdErrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await p.StandardError.ReadLineAsync()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    Status("Script Fehler: " + line.Trim());
                    TryParseAndSetProgress(line);
                }
            }
        });

        await p.WaitForExitAsync();
        await Task.WhenAll(stdOutTask, stdErrTask);

        return p.ExitCode;
    }

    private void TryParseAndSetProgress(string line)
    {
        var progressMatch = System.Text.RegularExpressions.Regex
            .Match(line, @"(?i)\bPROGRESS\b\s*:\s*(\d{1,3})");

        if (progressMatch.Success &&
            int.TryParse(progressMatch.Groups[1].Value, out var p))
        {
            SetProgressIndeterminate(false);
            SetProgress(Math.Clamp(p, 0, 100));
        }
    }

    private void Status(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusText.Text = text;
        });
    }

    private void SetProgress(double value)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateProgress.Value = value;
        });
    }

    private void SetProgressIndeterminate(bool indeterminate)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateProgress.IsIndeterminate = indeterminate;
        });
    }

    private void Button_Click_Settings(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            string buttonTag = button.Tag?.ToString() ?? "";

            if (buttonTag == "Menu")
            {
                if (!NavBarStatus)
                {
                    NavBar.Width += 100;
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
                    NavBarStatus = false;
                }
            }
            else
            {
                switch (buttonTag)
                {
                    case "Settings":
                    case "Menu":
                        (Owner as Window)?.Hide();
                        this.Hide();
                        break;
                }
            }
        }
    }
}
