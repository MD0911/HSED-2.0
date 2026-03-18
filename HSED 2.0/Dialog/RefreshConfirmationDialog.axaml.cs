using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HSED_2._0;

public partial class RefreshConfirmationDialog : Window
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    public RefreshConfirmationDialog()
    {
        InitializeComponent();

        Closing += (_, __) =>
        {
            if (!_tcs.Task.IsCompleted)
                _tcs.TrySetResult(false);
        };
    }

    public Task<bool> WaitForChoiceAsync() => _tcs.Task;

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(true);
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(false);
        Close();
    }
}
