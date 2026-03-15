// ViewModels/WifiViewModel.cs
using System.Collections.Generic;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using HSED_2._0.Models;

namespace HSED_2._0.ViewModels;

public sealed class WifiViewModel : ViewModelBase
{
    // Passe das an deinen echten Pfad an, damit es auf allen Pis unabhängig vom Working Directory funktioniert
    // Beispiel: "/opt/hsed/LinuxScripts"
    private const string ScriptsDir = "LinuxScripts";

    public ObservableCollection<WifiNetworkItem> Networks { get; } = new();
    public Task ConnectNowAsync() => ConnectAsync();


    private WifiNetworkItem? _selectedNetwork;
    public WifiNetworkItem? SelectedNetwork
    {
        get => _selectedNetwork;
        set
        {
            _selectedNetwork = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedInfo));
        }
    }

    private string _wifiPassword = "";
    public string WifiPassword
    {
        get => _wifiPassword;
        set
        {
            _wifiPassword = value;
            OnPropertyChanged();
        }
    }

    private string _statusText = "Bereit";
    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    private bool _isKeyboardVisible;
    public bool IsKeyboardVisible
    {
        get => _isKeyboardVisible;
        set
        {
            _isKeyboardVisible = value;
            OnPropertyChanged();
        }
    }

    public string SelectedInfo
    {
        get
        {
            if (SelectedNetwork is null) return "Kein WLAN ausgewählt";
            return $"Ausgewählt: {SelectedNetwork.Ssid}   Security: {SelectedNetwork.Security}";
        }
    }

    public ICommand ScanCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand HideKeyboardCommand { get; }

    public WifiViewModel()
    {
        ScanCommand = new AsyncCommand(ScanAsync);
        ConnectCommand = new AsyncCommand(ConnectAsync);
        HideKeyboardCommand = new SimpleCommand(() => IsKeyboardVisible = false);
    }

    // Keyboard helpers
    public void AppendKey(string key) => WifiPassword += key;

    public void Backspace()
    {
        if (WifiPassword.Length == 0) return;
        WifiPassword = WifiPassword.Substring(0, WifiPassword.Length - 1);
    }

    public void ClearPassword() => WifiPassword = "";

    private async Task ScanAsync()
    {
        StatusText = "Scan läuft";
        try
        {
            string script = $"{ScriptsDir}/scan_wifi.sh";
            string json = await RunScriptAsync(script, "wlan0", useSudo: true);

            var items = JsonSerializer.Deserialize<List<WifiNetworkItem>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            ) ?? new();

            Networks.Clear();

            foreach (var it in items)
            {
                if (!string.IsNullOrWhiteSpace(it.Ssid))
                    Networks.Add(it);
            }

            StatusText = $"Scan fertig: {Networks.Count} gefunden";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan Fehler: {ex.Message}";
        }
    }

    private async Task ConnectAsync()
    {
        if (SelectedNetwork is null)
        {
            StatusText = "Bitte WLAN auswählen";
            return;
        }

        StatusText = "Verbinde";
        try
        {
            string script = $"{ScriptsDir}/connect_wifi.sh";

            // Argumente sauber quoten
            string ssidArg = QuoteArg(SelectedNetwork.Ssid);
            string pskArg = QuoteArg(WifiPassword);

            string json = await RunScriptAsync(script, $"{ssidArg} {pskArg}", useSudo: true);

            StatusText = json;
        }
        catch (Exception ex)
        {
            StatusText = $"Connect Fehler: {ex.Message}";
        }
    }

    private static string QuoteArg(string s)
    {
        // Für bash sh Aufruf: " ... " und Quotes escapen
        return $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    private static async Task<string> RunScriptAsync(string scriptPath, string args, bool useSudo)
    {
        // Lösung B: immer über /bin/sh starten, damit keine +x Rechte nötig sind
        string fileName;
        string arguments;

        if (useSudo)
        {
            fileName = "sudo";
            arguments = $"/bin/sh \"{scriptPath}\" {args}";
        }
        else
        {
            fileName = "/bin/sh";
            arguments = $"\"{scriptPath}\" {args}";
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi) ?? throw new Exception("Process Start fehlgeschlagen");

        string stdout = await p.StandardOutput.ReadToEndAsync();
        string stderr = await p.StandardError.ReadToEndAsync();

        await p.WaitForExitAsync();

        if (p.ExitCode != 0)
            throw new Exception(string.IsNullOrWhiteSpace(stderr) ? $"ExitCode {p.ExitCode}" : stderr.Trim());

        return stdout.Trim();
    }
}

// AsyncCommand
public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _run;
    private bool _busy;

    public AsyncCommand(Func<Task> run) => _run = run;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_busy;

    public async void Execute(object? parameter)
    {
        if (_busy) return;
        _busy = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        try { await _run(); }
        finally
        {
            _busy = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

// SimpleCommand
public sealed class SimpleCommand : ICommand
{
    private readonly Action _action;

    public SimpleCommand(Action action) => _action = action;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _action();
}
