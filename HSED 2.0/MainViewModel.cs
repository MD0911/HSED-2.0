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

        private int _ls1;
        public int LS1
        {
            get => _ls1;
            set
            {
                if (_ls1 != value)
                {
                    _ls1 = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _ls2;
        public int LS2
        {
            get => _ls2;
            set
            {
                if (_ls2 != value)
                {
                    _ls2 = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _ls3;
        public int LS3
        {
            get => _ls3;
            set
            {
                if (_ls3 != value)
                {
                    _ls3 = value;
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

        private int _currentStateTueur1;
        public int CurrentStateTueur1
        {
            get => _currentStateTueur1;
            set
            {
                if (_currentStateTueur1 != value)
                {
                    _currentStateTueur1 = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _currentStateTueur2;
        public int CurrentStateTueur2
        {
            get => _currentStateTueur2;
            set
            {
                if (_currentStateTueur2 != value)
                {
                    _currentStateTueur2 = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _currentStateTueur3;
        public int CurrentStateTueur3
        {
            get => _currentStateTueur3;
            set
            {
                if (_currentStateTueur3 != value)
                {
                    _currentStateTueur3 = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _currentFahrkorb;
        public int CurrentFahrkorb
        {
            get => _currentFahrkorb;
            set
            {
                if (_currentFahrkorb != value)
                {
                    _currentFahrkorb = value;
                    OnPropertyChanged();
                }
            }
        }

        private float _positionY;
        public float PositionY
        {
            get => _positionY;
            set
            {
                if (_positionY != value)
                {
                    _positionY = value;
                    OnPropertyChanged();
                }
            }
        }



        private int _innenruftasterquittungEtage;
        public int InnenruftasterquittungEtage
        {
            get => _innenruftasterquittungEtage;
            set
            {
                if (_innenruftasterquittungEtage != value)
                {
                    _innenruftasterquittungEtage = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _innenruftasterquittungZustand;
        public int InnenruftasterquittungZustand
        {
            get => _innenruftasterquittungZustand;
            set
            {
                if (_innenruftasterquittungZustand != value)
                {
                    _innenruftasterquittungZustand = value;
                    OnPropertyChanged();
                }
            }
        }


        private int _aufAruftasterquittungEtage;
        public int AufAruftasterquittungEtage
        {
            get => _aufAruftasterquittungEtage;
            set
            {
                if (_aufAruftasterquittungEtage != value)
                {
                    _aufAruftasterquittungEtage = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _aufAruftasterquittungZustand;
        public int AufAruftasterquittungZustand
        {
            get => _aufAruftasterquittungZustand;
            set
            {
                if (_aufAruftasterquittungZustand != value)
                {
                    _aufAruftasterquittungZustand = value;
                    OnPropertyChanged();
                }
            }
        }


        private int _abAruftasterquittungEtage;
        public int AbAruftasterquittungEtage
        {
            get => _abAruftasterquittungEtage;
            set
            {
                if (_abAruftasterquittungEtage != value)
                {
                    _abAruftasterquittungEtage = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _abAruftasterquittungZustand;
        public int AbAruftasterquittungZustand
        {
            get => _abAruftasterquittungZustand;
            set
            {
                if (_abAruftasterquittungZustand != value)
                {
                    _abAruftasterquittungZustand = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _speed;
        public int Speed
        {
            get => _speed;
            set
            {
                if (_speed != value)
                {
                    _speed = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _signal;
        public int Signal
        {
            get => _signal;
            set
            {
                if (_signal != value)
                {
                    _signal = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _firstRide;
        public bool FirstRide
        {
            get => _firstRide;
            set
            {
                if (_firstRide != value)
                {
                    _firstRide = value;
                    OnPropertyChanged();
                }
            }
        }


        private bool _dop1;
        public bool DOP1
        {
            get => _dop1;
            set
            {
                if (_dop1 != value)
                {
                    _dop1 = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _dop2;
        public bool DOP2
        {
            get => _dop2;
            set
            {
                if (_dop2 != value)
                {
                    _dop2 = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _dop3;
        public bool DOP3
        {
            get => _dop3;
            set
            {
                if (_dop3 != value)
                {
                    _dop3 = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _dcl1;
        public bool DCL1
        {
            get => _dcl1;
            set
            {
                if (_dcl1 != value)
                {
                    _dcl1 = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _dcl2;
        public bool DCL2
        {
            get => _dcl2;
            set
            {
                if (_dcl2 != value)
                {
                    _dcl2 = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _dcl3;
        public bool DCL3
        {
            get => _dcl3;
            set
            {
                if (_dcl3 != value)
                {
                    _dcl3 = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _drev1;
        public bool DREV1
        {
            get => _drev1;
            set
            {
                if (_drev1 != value)
                {
                    _drev1 = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _drev2;
        public bool DREV2
        {
            get => _drev2;
            set
            {
                if (_drev2 != value)
                {
                    _drev2 = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _drev3;
        public bool DREV3
        {
            get => _drev3;
            set
            {
                if (_drev3 != value)
                {
                    _drev3 = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _dopna1;
        public bool DOPNA1
        {
            get => _dopna1;
            set
            {
                if (_dopna1 != value)
                {
                    _dopna1 = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _dopna2;
        public bool DOPNA2
        {
            get => _dopna2;
            set
            {
                if (_dopna2 != value)
                {
                    _dopna2 = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _dopna3;
        public bool DOPNA3
        {
            get => _dopna3;
            set
            {
                if (_dopna3 != value)
                {
                    _dopna3 = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _sgm;
        public bool SGM
        {
            get => _sgm;
            set
            {
                if (_sgm != value)
                {
                    _sgm = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _sgo;
        public bool SGO
        {
            get => _sgo;
            set
            {
                if (_sgo != value)
                {
                    _sgo = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _sgu;
        public bool SGU
        {
            get => _sgu;
            set
            {
                if (_sgu != value)
                {
                    _sgu = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _skf;
        public int SKF
        {
            get => _skf;
            set
            {
                if (_skf != value)
                {
                    _skf = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _rawCurrentFloor;
        public int RawCurrentFloor
        {
            get => _rawCurrentFloor;
            set
            {
                if (_rawCurrentFloor != value)
                {
                    _rawCurrentFloor = value;
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
