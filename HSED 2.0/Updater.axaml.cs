using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HSED_2._0.Models;
using System.IO;
using Avalonia;
using Avalonia.Input;


namespace HSED_2._0;

public partial class Updater : Window
{
    bool NavBarStatus = false;

    public Updater()
    {
        InitializeComponent();
        Position = new PixelPoint(0, 0);
        ReleaseScroll.AddHandler(InputElement.PointerWheelChangedEvent,
        OnWheel, RoutingStrategies.Tunnel);

        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }


    public void StartUpdater()
    {
        _ = LoadReleases();
    }

    private async Task LoadReleases()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("HSED-Updater");

            var json = await client.GetStringAsync(
                "https://api.github.com/repos/MD0911/HSED-2.0/releases");

            using var doc = JsonDocument.Parse(json);


            var releases = new List<ReleaseItem>();

            foreach (var r in doc.RootElement.EnumerateArray())
            {
                if (r.GetProperty("prerelease").GetBoolean())
                    continue;

                releases.Add(new ReleaseItem
                {
                    Tag = r.GetProperty("tag_name").GetString(),
                    Published = r.GetProperty("published_at").GetDateTime()
                });
            }

            var ordered = releases
                .OrderByDescending(r => r.Published)
                .ToList();
            if (ordered.Any())
            {
                ordered[0].IsLatest = true;
            }


            ReleaseList.ItemsSource = ordered;

            if (ordered.Any())
            {
                ReleaseList.SelectedIndex = 0;
                UpdateButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            // optional: Log oder Error-Text anzeigen
            UpdateButton.IsEnabled = false;
        }
    }

    private async void UpdateButton_Click(object? sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        SetProgress(0);
        SetProgressIndeterminate(true);

        try
        {
            if (ReleaseList.SelectedItem is not ReleaseItem selected)
            {
                Status("Bitte zuerst eine Version auswählen");
                return;
            }

            Status($"Update wird gestartet: {selected.Tag}");

            var scriptPath = ResolveUpdateScriptPath();
            if (!File.Exists(scriptPath))
            {
                Status($"Script nicht gefunden: {scriptPath}");
                return;
            }

            // Script ausführbar machen
            var chmodExit = await RunProcessAsync("/bin/chmod", $"+x \"{scriptPath}\"");
            if (chmodExit != 0)
            {
                Status($"chmod fehlgeschlagen (ExitCode {chmodExit}).");
                return;
            }

            // Script starten und WARTEN
            var args = $"\"{scriptPath}\" --version \"{selected.Tag}\"";
            Status("Update läuft... bitte Gerät nicht ausschalten.\nLog: /tmp/hsed_update.log");

            var exit = await RunProcessAsync("/bin/bash", args);

            // Wenn wir hier landen, hat es NICHT rebootet (sonst wäre die App weg)
            Status($"Update Script beendet (ExitCode {exit}).\nSiehe Log: /tmp/hsed_update.log");
        }
        catch (Exception ex)
        {
            Status("Fehler beim Starten des Updates: " + ex.Message);
        }
        finally
        {
            SetProgressIndeterminate(false);
            SetProgress(100);
            UpdateButton.IsEnabled = true;
        }
    }




    private string ResolveUpdateScriptPath()
    {
        // Deine Release Struktur: LinuxScripts/update_hsed.sh im Output
        // AppContext.BaseDirectory ist auf dem Pi dein Programmordner
        return Path.Combine(AppContext.BaseDirectory, "LinuxScripts", "update_hsed.sh");
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
        var percentMatch = System.Text.RegularExpressions.Regex
            .Match(line, @"(?<!\d)(\d{1,3})\s*%");

        if (percentMatch.Success &&
            int.TryParse(percentMatch.Groups[1].Value, out var percent))
        {
            SetProgressIndeterminate(false);
            SetProgress(Math.Clamp(percent, 0, 100));
            return;
        }

        var progressMatch = System.Text.RegularExpressions.Regex
            .Match(line, @"(?i)\bprogress\b\s*[:=]\s*(\d{1,3})");

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
            if (StatusText != null)
                StatusText.Text = text;
        });
    }

    private void SetProgress(double value)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (UpdateProgress != null)
                UpdateProgress.Value = value;
        });
    }

    private void SetProgressIndeterminate(bool indeterminate)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (UpdateProgress != null)
                UpdateProgress.IsIndeterminate = indeterminate;
        });
    }

    private void OnWheel(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (sender is not Avalonia.Controls.ScrollViewer sv) return;

        // Wheel Y (normal vertikal) wird in X umgeleitet
        var delta = e.Delta.Y * 40; // Faktor nach Gefühl
        sv.Offset = sv.Offset.WithX(sv.Offset.X - delta);

        e.Handled = true;
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
                        WindowNavigationService.ReturnToSettings(this);
                        break;
                    case "Home":
                        WindowNavigationService.NavigateHome(this);
                        break;
                    case "SelfDia":
                        _ = TouchDisplayRefreshService.RequestRefreshAsync(this);
                        break;


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


                }
            }
        }
    }
}
