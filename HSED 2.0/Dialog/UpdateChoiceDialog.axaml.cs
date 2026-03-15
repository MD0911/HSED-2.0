using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HSED_2._0;

public enum UpdateMode
{
    None = 0,
    Online = 1,
    OfflineUsb = 2
}

public partial class UpdateChoiceDialog : Window
{
    private readonly TaskCompletionSource<UpdateMode> _tcs = new();

    public UpdateChoiceDialog()
    {
        InitializeComponent();

        Closing += (_, __) =>
        {
            if (!_tcs.Task.IsCompleted)
                _tcs.TrySetResult(UpdateMode.None);
        };
    }

    public Task<UpdateMode> WaitForChoiceAsync() => _tcs.Task;

    private void Online_Click(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(UpdateMode.Online);
        Close();
    }

    private void Offline_Click(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(UpdateMode.OfflineUsb);
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(UpdateMode.None);
        Close();
    }
}
