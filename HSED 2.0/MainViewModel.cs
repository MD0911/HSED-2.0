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

        private int _currentTemp;
        public int CurrentTemp
        {
            get => _currentTemp;
            set
            {
                if (_currentTemp != value)
                {
                    _currentTemp = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _currentSK1;
        public int CurrentSK1
        {
            get => _currentSK1;
            set
            {
                if (_currentSK1 != value)
                {
                    _currentSK1 = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _currentSK2;
        public int CurrentSK2
        {
            get => _currentSK2;
            set
            {
                if (_currentSK2 != value)
                {
                    _currentSK2 = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _currentSK3;
        public int CurrentSK3
        {
            get => _currentSK3;
            set
            {
                if (_currentSK3 != value)
                {
                    _currentSK3 = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _currentSK4;
        public int CurrentSK4
        {
            get => _currentSK4;
            set
            {
                if (_currentSK4 != value)
                {
                    _currentSK4 = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _currentLast;
        public int CurrentLast
        {
            get => _currentLast;
            set
            {
                if (_currentLast != value)
                {
                    _currentLast = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _currentZustand;
        public int CurrentZustand
        {
            get => _currentZustand;
            set
            {
                if (_currentZustand != value)
                {
                    _currentZustand = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _currentfahrtzahler;
        public int CurrentFahrtZahler
        {
            get => _currentfahrtzahler;
            set
            {
                if (_currentfahrtzahler != value)
                {
                    _currentfahrtzahler = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _currentBStunden;
        public int CurrentBStunden
        {
            get => _currentBStunden;
            set
            {
                if (_currentBStunden != value)
                {
                    _currentBStunden = value;
                    OnPropertyChanged();
                }
            }
        }

        // Weitere Properties (z. B. A-Zustand) können hier ergänzt werden.

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
