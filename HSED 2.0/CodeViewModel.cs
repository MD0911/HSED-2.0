using Avalonia.Threading;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace HSED_2._0
{
    public class CodeViewModel : INotifyPropertyChanged
    {
        private string _currentInput = "Kommando";
        public string CurrentInput
        {
            get => _currentInput;
            private set
            {
                if (_currentInput != value)
                {
                    _currentInput = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand NumberCommand { get; }
        public ICommand ActionCommand { get; }

        public CodeViewModel()
        {
            NumberCommand = new DelegateCommand(async param =>
            {
                if (param is not string digit || digit.Length != 1 || !char.IsDigit(digit[0]))
                    return;

                if (CurrentInput == "Kommando")
                    CurrentInput = "";

                CurrentInput += digit;
                await Task.CompletedTask;
            });

            ActionCommand = new DelegateCommand(async param =>
            {
                if (param is not string action)
                    return;

                if (action == "ESC")
                {
                    CurrentInput = "Kommando";
                    await Task.CompletedTask;
                }
                else if (action == "E")
                {
                    string toSend = CurrentInput;
                    CurrentInput = "Kommando";

                    // Asynchron ohne UI-Blockierung
                    _ = SendTelegramAsync(toSend);
                }
            });
        }

        private async Task SendTelegramAsync(string inputText)
        {
            foreach (char c in inputText)
            {
                if (char.IsDigit(c))
                {
                    byte asciiByte = (byte)c;
                    SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, asciiByte });

                    // 10 ms Pause pro Zeichen, statt 100 ms
                    await Task.Delay(10);
                }
            }

            // Terminator-Byte
            SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x01, 0x03, 0x00, 0x0D });
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            // Stelle sicher, dass das Event auf dem UI-Thread ausgelöst wird
            Dispatcher.UIThread.Post(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }

        #endregion

        #region DelegateCommand

        private class DelegateCommand : ICommand
        {
            private readonly Func<object?, Task> _execute;
            private readonly Func<object?, bool>? _canExecute;

            public DelegateCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
            {
                _execute = execute;
                _canExecute = canExecute;
            }

            public event EventHandler? CanExecuteChanged
            {
                // Avalonia braucht hier keine Implementation, 
                // leere Add/Remove verhindern Fehler
                add { }
                remove { }
            }

            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

            public async void Execute(object? parameter) => await _execute(parameter);
        }

        #endregion
    }
}
