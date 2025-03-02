using Avalonia.Controls;
using System;
using System.IO.Ports;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading;
using System.Diagnostics;
using HSED_2_0;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Media;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Drawing;


namespace HSED_2._0
{
    public partial class TestrufeNeu : Window
    {
        private CancellationTokenSource _cancellationTokenSource;
        bool NavBarStatus = false;
        private DispatcherTimer _blinkTimer;
        private bool _isGreen = false;
        private int ZielEtage;
        private bool isAussenRuf;
        private bool AussenRufisUp;

        public TestrufeNeu()
        {
            InitializeComponent();
            this.Position = new Avalonia.PixelPoint(100, 100);
            HseConnect();
            _cancellationTokenSource = new CancellationTokenSource();
            StartPeriodicUpdateO(TimeSpan.FromSeconds(1), _cancellationTokenSource.Token);
            StartPeriodicUpdate(TimeSpan.FromSeconds(30), _cancellationTokenSource.Token);
            StartPeriodicUpdateBlink(TimeSpan.FromSeconds(6), _cancellationTokenSource.Token);
        }

        private async void StartPeriodicUpdateO(TimeSpan interval, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    HseUpdatedO(); // Update-Methode aufrufen
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
                try
                {
                    await Task.Delay(interval, token);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }

        private async void AnimateProgressBar(int targetValue)
        {
            var progressBar = this.FindControl<ProgressBar>("EtageProgressBar");
            if (progressBar == null) return;

            double currentValue = progressBar.Value;
            double step = 0.1 * Math.Sign(targetValue - currentValue);

            while (Math.Abs(targetValue - currentValue) > Math.Abs(step))
            {
                currentValue += step;
                progressBar.Value = currentValue;
                await Task.Delay(15);
            }
            progressBar.Value = targetValue;
        }

        private async void StartPeriodicUpdateBlink(TimeSpan interval, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    SK(); // Update-Methode aufrufen
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
                try
                {
                    await Task.Delay(interval, token);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }

        private async void StartPeriodicUpdate(TimeSpan interval, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    HseUpdated(); // Update-Methode aufrufen
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
                try
                {
                    await Task.Delay(interval, token);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }

        private void HseConnect()
        {
            try
            {
                byte[] bottomfloor = new byte[12];
                byte[] currentfloor = new byte[12];
                bottomfloor = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
                int topFloor = bottomfloor[10];
                byte[] bottomfloorName = new byte[2];
                bottomfloorName[0] = bottomfloor[11];
                bottomfloorName[1] = bottomfloor[10];
                string asciiString = Encoding.ASCII.GetString(bottomfloorName);
                int bootFloor = Convert.ToInt32(asciiString);
                int AllFloorsTop = HseCom.SendHse(1001);

                int obersteEtagenBezeichnung = (AllFloorsTop + bootFloor) - 1;

                EtagenInsgesamtOberste.Text = obersteEtagenBezeichnung.ToString();
                EtagenInsgesamtUnterste.Text = bootFloor.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }

            int allFloors = HseCom.SendHse(1001);
            EtageProgressBar.Maximum = allFloors - 1;
            int currentfloorBezeichnung = HseCom.SendHse(1002);
            Etage.Text = currentfloorBezeichnung.ToString();
        }

        private void SK()
        {
            Zustand.Foreground = new SolidColorBrush(Colors.Gray);
            Tuer1.Foreground = new SolidColorBrush(Colors.Gray);
            Tuer2.Foreground = new SolidColorBrush(Colors.Gray);
            SK1.Background = new SolidColorBrush(Colors.Gray);
            SK2.Background = new SolidColorBrush(Colors.Gray);
            SK3.Background = new SolidColorBrush(Colors.Gray);
            SK4.Background = new SolidColorBrush(Colors.Gray);
        }

        private void HseUpdatedO()
        {
            var skBorders = new Border[] { SK1, SK2, SK3, SK4 };
            int GanzeSK = HseCom.SendHse(1003);
            int[] SK = HseCom.IntToArray(GanzeSK);

            for (int i = 0; i < SK.Length; i++)
            {
                if (SK[i] == 0)
                {
                    skBorders[i].Background = new SolidColorBrush(Colors.Red);
                }
                else
                {
                    skBorders[i].Background = new SolidColorBrush(Colors.GreenYellow);
                }
            }

            int currentfloor = HseCom.SendHse(1002);
            if (currentfloor == 505 || currentfloor == 404)
                return;

            Etage.Text = currentfloor.ToString();

            int AZustand = HseCom.SendHse(1005);
            if (AZustand == 505 || AZustand == 404)
                return;

            int tuerZustand1 = HseCom.SendHse(1006);
            int tuerZustand2 = HseCom.SendHse(1016);

            switch (tuerZustand1)
            {
                case 0:
                    Tuer1.Text = "Geschlossen";
                    Tuer1.Foreground = new SolidColorBrush(Colors.White);
                    break;
                case 50:
                    Tuer1.Text = "Tür öffnet";
                    Tuer1.Foreground = new SolidColorBrush(Colors.Yellow);
                    break;
                case 48:
                    Tuer1.Text = "Tür geöffnet";
                    Tuer1.Foreground = new SolidColorBrush(Colors.GreenYellow);
                    break;
                case 32:
                    Tuer1.Text = "Tür schließt";
                    Tuer1.Foreground = new SolidColorBrush(Colors.Yellow);
                    break;
                case 96:
                    Tuer1.Text = "LS unterbrochen";
                    Tuer1.Foreground = new SolidColorBrush(Colors.Orange);
                    break;
                case 97:
                    Tuer1.Text = "Tür geöffnet";
                    Tuer1.Foreground = new SolidColorBrush(Colors.GreenYellow);
                    break;
                case 224:
                    Tuer1.Text = "Türfehler";
                    Tuer1.Foreground = new SolidColorBrush(Colors.Red);
                    break;
                case 112:
                    Tuer1.Text = "Tür gestoppt";
                    Tuer1.Foreground = new SolidColorBrush(Colors.Red);
                    break;
            }

            switch (tuerZustand2)
            {
                case 0:
                    Tuer2.Text = "Geschlossen";
                    Tuer2.Foreground = new SolidColorBrush(Colors.White);
                    break;
                case 50:
                    Tuer2.Text = "Tür öffnet";
                    Tuer2.Foreground = new SolidColorBrush(Colors.Yellow);
                    break;
                case 48:
                    Tuer2.Text = "Tür geöffnet";
                    Tuer2.Foreground = new SolidColorBrush(Colors.GreenYellow);
                    break;
                case 32:
                    Tuer2.Text = "Tür schließt";
                    Tuer2.Foreground = new SolidColorBrush(Colors.Yellow);
                    break;
                case 96:
                    Tuer2.Text = "LS unterbrochen";
                    Tuer2.Foreground = new SolidColorBrush(Colors.Orange);
                    break;
                case 97:
                    Tuer2.Text = "Tür geöffnet";
                    Tuer2.Foreground = new SolidColorBrush(Colors.GreenYellow);
                    break;
                case 224:
                    Tuer2.Text = "Türfehler";
                    Tuer2.Foreground = new SolidColorBrush(Colors.Red);
                    break;
                case 112:
                    Tuer2.Text = "Tür gestoppt";
                    Tuer2.Foreground = new SolidColorBrush(Colors.Red);
                    break;
            }

            switch (AZustand)
            {
                case 4:
                    Zustand.Text = "Stillstand";
                    Zustand.Foreground = new SolidColorBrush(Colors.White);
                    break;
                case 5:
                    Zustand.Text = "Fährt";
                    Zustand.Foreground = new SolidColorBrush(Colors.GreenYellow);
                    break;
                case 6:
                    Zustand.Text = "Einfahrt";
                    Zustand.Foreground = new SolidColorBrush(Colors.Yellow);
                    break;
                case 17:
                    Zustand.Text = "SK Fehlt";
                    Zustand.Foreground = new SolidColorBrush(Colors.Red);
                    break;
            }
            EtageProgressBar.Value = currentfloor + 1;
            //AnimateProgressBar(HseCom.SendHse(1004));
        }

        private void HseUpdated()
        {
            // Zusätzliche Update-Logik (falls benötigt)
        }

        private void Button_Click_Settings(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                string buttonTag = button.Tag?.ToString();
                if (buttonTag == "Menu")
                {
                    if (NavBarStatus == false)
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
                        SettingsText5.IsVisible = true;
                        ButtonSettings5.Width = 100;
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
                        SettingsText5.IsVisible = false;
                        ButtonSettings5.Width = 50;
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
                            var newWindowSettings = new Settings();
                            newWindowSettings.Show();
                            break;
                        case "Testrufe":
                            var newWindowTestrufe = new TestrufeNeu();
                            newWindowTestrufe.Show();
                            this.Close();
                            break;
                        case "Codes":
                            var newWindowCode = new Code();
                            newWindowCode.Show();
                            break;
                        case "SelfDia":
                            var newWindowDia = new LiveViewAnimationSimulation();
                            newWindowDia.Show();
                            
                            break;
                        case "Ansicht":
                            TerminalManager.terminalActive = true;
                            var newWindowAnsicht = new Terminal();
                            newWindowAnsicht.Show();
                            break;

                        case "Home":
                            var newWindowHome = new MainWindow();
                            newWindowHome.Show();
                            this.Close();
                            break;
                    }
                }
            }
        }

        private void Button_Click_X(object? sender, RoutedEventArgs e)
        {
            IoA.IsVisible = false;
            AOoU.IsVisible = false;
        }

        private void Button_Click_IoA(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                
                byte floor = (byte)ZielEtage;
                switch(button.Tag?.ToString())
                {
                    case "I":
                        isAussenRuf = false;
                        IoA.IsVisible = false;
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] {0x04, 0x01, 0x05, floor, 0x01, 0x00, 0x01, 0x01});
                        break;
                    case "A":
                        IoA.IsVisible = false;
                        AOoU.IsVisible = true;
                        break;
                }
            }
        }

      

        private void Button_Click_AOoU(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                byte floor = (byte)ZielEtage;
                switch (button.Tag?.ToString())
                {
                    case "R":
                        AOoU.IsVisible = false;
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x04, 0x01, 0x02, 0x02, 0x01, floor, 0x01, 0x01 });
                        break;
                    case "H":
                        AOoU.IsVisible = false;
                        SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x04, 0x01, 0x02, 0x01, 0x01, floor, 0x01, 0x01 });
                        break;
                }
            }
        }
        private void Button_Click_Number(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                string buttonTag = button.Content?.ToString();

                switch (buttonTag) {

                    case "-1":
                        ZielEtage = 1;
                        break;

                    case "0":
                        ZielEtage = 2;
                        break;

                    case "1":
                        ZielEtage = 3;
                        break;

                    case "2":
                        ZielEtage = 4;
                        break;

                    case "3":
                        ZielEtage = 5;
                    break;

                    case "4":
                        ZielEtage = 6;
                        break;

                    case "5":
                        ZielEtage = 7;
                        break;

                    case "6":
                        ZielEtage = 8;
                        break;


                }

                if (ZielEtage != null)
                {
                    IoA.IsVisible = true;
                }
             


            }
        }

        private void Button_Click_OMU(object? sender, RoutedEventArgs e)
        {
            if (sender is Button border)
            {
                string buttonTag = border.Tag?.ToString();
                if (buttonTag == "OFahren")
                {
                    MainWindow mainWindow = new MainWindow();
                    int floors = mainWindow.gesamteFloors;
                    byte bytefloors = (byte)floors;
                    //HseCom.SendHseCommand(new byte[] { 0x04, 0x01, 0x05, bytefloors, 0x01, 0x00, 0x01, 0x01 });
                    SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x04, 0x01, 0x05, bytefloors, 0x01, 0x00, 0x01, 0x01 });
                }
                if (buttonTag == "MFahren")
                {
                    int floors = HseCom.SendHse(1001);
                    floors = floors / 2;
                    byte bytefloors = (byte)floors;
                   // HseCom.SendHseCommand(new byte[] { 0x04, 0x01, 0x05, bytefloors, 0x01, 0x00, 0x01, 0x01 });
                    SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x04, 0x01, 0x05, bytefloors, 0x01, 0x00, 0x01, 0x01 });
                }
                if (buttonTag == "UFahren")
                {
                    //HseCom.SendHseCommand(new byte[] { 0x04, 0x01, 0x05, 0x01, 0x01, 0x00, 0x01, 0x01 });
                    SerialPortManager.Instance.SendWithoutResponse(new byte[] { 0x04, 0x01, 0x05, 0x01, 0x01, 0x00, 0x01, 0x01 });
                }
            }
        }

        private void Border_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (sender is Border border)
            {
                string buttonTag = border.Tag?.ToString();
                if (buttonTag == "OFahren")
                {
                    MainWindow mainWindow = new MainWindow();
                    int floors = mainWindow.gesamteFloors;
                    byte bytefloors = (byte)floors;
                    HseCom.SendHseCommand(new byte[] { 0x04, 0x01, 0x05, bytefloors, 0x01, 0x00, 0x01, 0x01 });
                }
                if (buttonTag == "MFahren")
                {
                    int floors = HseCom.SendHse(1001);
                    floors = floors / 2;
                    byte bytefloors = (byte)floors;
                    HseCom.SendHseCommand(new byte[] { 0x04, 0x01, 0x05, bytefloors, 0x01, 0x00, 0x01, 0x01 });
                }
                if (buttonTag == "UFahren")
                {
                    HseCom.SendHseCommand(new byte[] { 0x04, 0x01, 0x05, 0x01, 0x01, 0x00, 0x01, 0x01 });
                }
            }
        }

        private void Border_PointerPressed_1(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (Input.Text == "Etagenwert" || Input.Text == "Etage exestiert nicht!" || Input.Text == "Bitte erneut probieren.")
                Input.Text = "";
            if (Input.Text == "0")
                Input.Text = "-";

            if (sender is Border border)
            {
                string buttonTag = border.Tag?.ToString();
                switch (buttonTag)
                {
                    case "0":
                        Input.Text = Input.Text + "0";
                        break;
                    case "1":
                        Input.Text = Input.Text + "1";
                        break;
                    case "2":
                        Input.Text = Input.Text + "2";
                        break;
                    case "3":
                        Input.Text = Input.Text + "3";
                        break;
                    case "4":
                        Input.Text = Input.Text + "4";
                        break;
                    case "5":
                        Input.Text = Input.Text + "5";
                        break;
                    case "6":
                        Input.Text = Input.Text + "6";
                        break;
                    case "7":
                        Input.Text = Input.Text + "7";
                        break;
                    case "8":
                        Input.Text = Input.Text + "8";
                        break;
                    case "9":
                        Input.Text = Input.Text + "9";
                        break;
                }
            }
        }

        private void Border_PointerPressed_2(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (sender is Border border)
            {
                string buttonTag = border.Tag?.ToString();
                byte byteFloors;
                if (Input.Text == "Etagenwert" || Input.Text == "Etage exestiert nicht!" || Input.Text == "Bitte erneut probieren.")
                    return;

                int input = Convert.ToInt32(Input.Text);

                if (input >= 100)
                {
                    Input.Text = "Etage exestiert nicht!";
                }
                else
                {
                    byte[] bottomfloor = new byte[12];
                    byte[] currentfloor = new byte[12];
                    bottomfloor = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
                    int topFloor = bottomfloor[10];
                    byte[] bottomfloorName = new byte[2];
                    bottomfloorName[0] = bottomfloor[11];
                    bottomfloorName[1] = bottomfloor[10];
                    string asciiString = Encoding.ASCII.GetString(bottomfloorName);
                    int bootFloor = Convert.ToInt32(asciiString);
                    int AllFloorsTop = HseCom.SendHse(1001);
                    int obersteEtagenBezeichnung = (AllFloorsTop + bootFloor) - 1;
                    if (input > obersteEtagenBezeichnung)
                    {
                        Input.Text = "Etage exestiert nicht!";
                    }
                    else
                    {
                        if (buttonTag == "I")
                        {
                            int bootFloor2 = Convert.ToInt32(asciiString);
                            input = input - bootFloor2 + 1;
                            byteFloors = (byte)input;
                            HseCom.SendHseCommand(new byte[] { 0x04, 0x01, 0x05, byteFloors, 0x01, 0x00, 0x01, 0x01 });
                            Input.Text = "Etagenwert";
                        }
                        else if (buttonTag == "A")
                        {
                            Numpad.IsVisible = false;
                            Aussengruppe.IsVisible = true;
                        }
                        else
                        {
                            Input.Text = "Bitte erneut probieren.";
                        }
                    }
                }
            }
        }

        

        private void Border_PointerPressed_3(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (sender is Border border)
            {
                string buttonTag = border.Tag?.ToString();
                int input = Convert.ToInt32(Input.Text);

                if (buttonTag == "Hoch")
                {
                    byte[] bottomfloor2 = new byte[12];
                    byte[] currentfloor2 = new byte[12];
                    bottomfloor2 = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
                    int topFloor2 = bottomfloor2[10];
                    byte[] bottomfloorName2 = new byte[2];
                    bottomfloorName2[0] = bottomfloor2[11];
                    bottomfloorName2[1] = bottomfloor2[10];
                    string asciiString2 = Encoding.ASCII.GetString(bottomfloorName2);
                    int bootFloor2 = Convert.ToInt32(asciiString2);

                    input = input - bootFloor2 + 1;
                    byte byteFloors = (byte)input;
                    HseCom.SendHseCommand(new byte[] { 0x04, 0x01, 0x02, 0x01, 0x01, byteFloors, 0x01, 0x01 });
                    Input.Text = "Etagenwert";
                    Numpad.IsVisible = true;
                    Aussengruppe.IsVisible = false;
                }

                if (buttonTag == "Runter")
                {
                    byte[] bottomfloor2 = new byte[12];
                    byte[] currentfloor2 = new byte[12];
                    bottomfloor2 = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
                    int topFloor2 = bottomfloor2[10];
                    byte[] bottomfloorName2 = new byte[2];
                    bottomfloorName2[0] = bottomfloor2[11];
                    bottomfloorName2[1] = bottomfloor2[10];
                    string asciiString2 = Encoding.ASCII.GetString(bottomfloorName2);
                    int bootFloor2 = Convert.ToInt32(asciiString2);

                    input = input - bootFloor2 + 1;
                    byte byteFloors = (byte)input;
                    HseCom.SendHseCommand(new byte[] { 0x04, 0x01, 0x02, 0x02, 0x01, byteFloors, 0x01, 0x01 });
                    Input.Text = "Etagenwert";
                    Numpad.IsVisible = true;
                    Aussengruppe.IsVisible = false;
                }
            }
        }

        private void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
        }
    }
}
