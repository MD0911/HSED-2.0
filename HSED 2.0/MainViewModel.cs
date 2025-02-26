using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HSED_2_0.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private int _currentFloor;
        public int CurrentFloor
        {
            get => _currentFloor;
            set
            {
                if (_currentFloor != value)
                {
                    _currentFloor = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _skValue;
        public int SKValue
        {
            get => _skValue;
            set
            {
                if (_skValue != value)
                {
                    _skValue = value;
                    OnPropertyChanged();
                }
            }
        }

        // Weitere Properties (z. B. A-Zustand) können hier ergänzt werden

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
