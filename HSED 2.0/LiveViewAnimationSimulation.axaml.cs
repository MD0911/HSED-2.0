using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace HSED_2._0
{
    public partial class LiveViewAnimationSimulation : Window
    {
        // Transform-Referenzen für den Aufzug und den animierten Text
        private TranslateTransform ElevatorAnimTransform;
        private TranslateTransform ElevatorDynamicTransform;
        private TranslateTransform NumberTextTransform;

        private CancellationTokenSource _ctsElevator;
        private CancellationTokenSource _ctsBackground;
        private CancellationTokenSource _ctsDynamic;
        private CancellationTokenSource _ctsBlinkDoors;
        private CancellationTokenSource _ctsBlinkDots;
        private CancellationTokenSource _ctsClock; // für die Uhrzeit

        // Basisposition des Aufzugs (Y-Wert)
        private const double BaseElevatorY = 0;

        // Parameter für die Hintergrundanimation
        private const int NumberOfLines = 10;
        private const double MaxSpeed = 15;
        private const double AccelerationRate = 0.2;
        private const double DecelerationRate = 0.2;
        private double backgroundSpeed = 0;
        private double targetSpeed = 0;

        // Parameter für den Blur-Effekt beim Aufzug
        private const double DynamicBlurMaxRadius = 1.5;
        private const double BlurLerpRate = 0.05;
        private const int BlurStartDelayMs = 1500;
        private double currentElevatorBlur = 0;
        private long _movementStartTime = 0;

        // Offscreen-Position (X-Richtung) für den animierten Text
        private const double offScreenX = -100;
        private const double threshold = 1.0;
        private const int verticalDelayMs = 200;

        // Zahl, die angezeigt wird
        private const string DisplayNumber = "6";
        private bool isDisplayNumberVisible = false;

        public LiveViewAnimationSimulation()
        {
            InitializeComponent();

            // Hole das TransformGroup des ElevatorView und extrahiere die beiden TranslateTransforms
            var tg = ElevatorView.RenderTransform as TransformGroup;
            ElevatorAnimTransform = tg.Children[0] as TranslateTransform;
            ElevatorDynamicTransform = tg.Children[1] as TranslateTransform;

            // Transform für den NumberText setzen
            if (NumberText.RenderTransform is TranslateTransform tt)
            {
                NumberTextTransform = tt;
            }
            else
            {
                NumberTextTransform = new TranslateTransform { X = offScreenX, Y = 0 };
                NumberText.RenderTransform = NumberTextTransform;
            }
            NumberText.Text = DisplayNumber;

            // Initialisierung der Hintergrundlinien
            InitializeBackgroundLines();
            _ctsBackground = new CancellationTokenSource();
            _ = AnimateBackgroundLinesAsync(_ctsBackground.Token);
            _ctsDynamic = new CancellationTokenSource();
            _ = AnimateElevatorDynamicMovementAsync(_ctsDynamic.Token);

            // Tür-Blinking (bestehende Animation)
            _ctsBlinkDoors = new CancellationTokenSource();
            _ = BlinkDoorsAsync(_ctsBlinkDoors.Token);

            // Blinkende Punkte innerhalb des Borders
            _ctsBlinkDots = new CancellationTokenSource();
            _ = BlinkDotsAsync(_ctsBlinkDots.Token);

            // Uhrzeit aktualisieren
            _ctsClock = new CancellationTokenSource();
            _ = UpdateClockAsync(_ctsClock.Token);

            // Event-Handler für die Steuerungs-Buttons
            this.FindControl<Button>("BtnUp").Click += BtnUp_Click;
            this.FindControl<Button>("BtnStop").Click += BtnStop_Click;
            this.FindControl<Button>("BtnDown").Click += BtnDown_Click;
        }

        private async Task UpdateClockAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // Setze die aktuelle Uhrzeit (HH:mm:ss)
                ClockText.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                await Task.Delay(1000, token);
            }
        }

        private void InitializeBackgroundLines()
        {
            double canvasHeight = WindCanvas.Bounds.Height > 0 ? WindCanvas.Bounds.Height : 395;
            double spacing = canvasHeight / NumberOfLines;
            WindCanvas.Children.Clear();

            const double shortLineLength = 40;
            const double longLineLength = 100;

            for (int i = 0; i < NumberOfLines; i++)
            {
                double lineLength = ((i + 1) % 5 == 0) ? longLineLength : shortLineLength;
                double y = i * spacing;
                var line = new Line
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(lineLength, 0),
                    Stroke = Brushes.LightBlue,
                    StrokeThickness = 2
                };
                Canvas.SetTop(line, y);
                Canvas.SetLeft(line, offScreenX);
                WindCanvas.Children.Add(line);
            }
        }

        #region Aufzugsanimation

        private async Task AnimateElevatorOffsetAsync(double totalOffset, int durationMs, CancellationToken token)
        {
            await Task.Delay(200, token);
            if (isDisplayNumberVisible)
            {
                await AnimateNumberTextOut(token);
            }

            double startTime = Environment.TickCount;
            double startPos = ElevatorAnimTransform.Y;
            double startValue = Math.Pow(2, -10);
            while (!token.IsCancellationRequested)
            {
                double elapsed = Environment.TickCount - startTime;
                double t = Math.Min(elapsed / (double)durationMs, 1.0);
                double eased = (Math.Pow(2, 10 * (t - 1)) - startValue) / (1 - startValue);
                ElevatorAnimTransform.Y = startPos + totalOffset * eased;
                if (t >= 1.0)
                    break;
                await Task.Delay(16, token);
            }
        }

        private async Task AnimateElevatorToCenterAsync(int durationMs, CancellationToken token)
        {
            await Task.Delay(200, token);
            double startTime = Environment.TickCount;
            double startPos = ElevatorAnimTransform.Y;
            double delta = BaseElevatorY - startPos;
            while (!token.IsCancellationRequested)
            {
                double elapsed = Environment.TickCount - startTime;
                double t = Math.Min(elapsed / (double)durationMs, 1.0);
                double eased = (t >= 1.0 ? 1.0 : 1 - Math.Pow(2, -10 * t));
                ElevatorAnimTransform.Y = startPos + delta * eased;
                if (t >= 1.0)
                    break;
                await Task.Delay(16, token);
            }
        }

        #endregion

        #region Dynamische Bewegung des Aufzugs

        private async Task AnimateElevatorDynamicMovementAsync(CancellationToken token)
        {
            const double baseAmplitudeX = 2;
            const double baseAmplitudeY = 1;
            const double frequency = 0.002;
            while (!token.IsCancellationRequested)
            {
                double t = Environment.TickCount;
                if (Math.Abs(targetSpeed) > 0)
                {
                    if (_movementStartTime == 0)
                        _movementStartTime = Environment.TickCount;

                    double amplitudeX = baseAmplitudeX * (Math.Abs(targetSpeed) / MaxSpeed);
                    double offsetX = amplitudeX * Math.Sin(t * frequency * 2 * Math.PI);
                    double offsetY = baseAmplitudeY * Math.Cos(t * frequency * 2 * Math.PI);
                    ElevatorDynamicTransform.X = offsetX;
                    ElevatorDynamicTransform.Y = offsetY;

                    long elapsedSinceStart = Environment.TickCount - _movementStartTime;
                    double desiredBlur = (elapsedSinceStart >= BlurStartDelayMs)
                        ? ((Math.Abs(targetSpeed) - 0.7 * MaxSpeed) / (MaxSpeed - 0.7 * MaxSpeed) * DynamicBlurMaxRadius)
                        : 0;
                    currentElevatorBlur = currentElevatorBlur + BlurLerpRate * (desiredBlur - currentElevatorBlur);
                }
                else
                {
                    ElevatorDynamicTransform.X = 0;
                    ElevatorDynamicTransform.Y = 0;
                    currentElevatorBlur = currentElevatorBlur + BlurLerpRate * (0 - currentElevatorBlur);
                    _movementStartTime = 0;
                }
                ElevatorView.Effect = (currentElevatorBlur > 0.1) ? new BlurEffect { Radius = currentElevatorBlur } : null;
                await Task.Delay(16, token);
            }
        }

        #endregion

        #region Hintergrundanimation

        private async Task AnimateBackgroundLinesAsync(CancellationToken token)
        {
            double canvasHeight = WindCanvas.Bounds.Height > 0 ? WindCanvas.Bounds.Height : 395;
            DateTime? verticalDelayStart = null;

            while (!token.IsCancellationRequested)
            {
                if (backgroundSpeed < targetSpeed)
                    backgroundSpeed = Math.Min(backgroundSpeed + AccelerationRate, targetSpeed);
                else if (backgroundSpeed > targetSpeed)
                    backgroundSpeed = Math.Max(backgroundSpeed - DecelerationRate, targetSpeed);

                double blurRadius = Math.Abs(backgroundSpeed) / MaxSpeed * 5;
                bool horizontalArrived = false;
                if (WindCanvas.Children.Count > 0 && WindCanvas.Children[0] is Line firstLine)
                {
                    double firstX = Canvas.GetLeft(firstLine);
                    horizontalArrived = Math.Abs(firstX) < threshold;
                }

                if (targetSpeed != 0)
                {
                    if (horizontalArrived)
                    {
                        if (verticalDelayStart == null)
                            verticalDelayStart = DateTime.Now;
                    }
                    else
                    {
                        verticalDelayStart = null;
                    }
                }
                else
                {
                    verticalDelayStart = null;
                }

                bool allowVertical = false;
                if (targetSpeed != 0 && verticalDelayStart != null)
                {
                    if ((DateTime.Now - verticalDelayStart.Value).TotalMilliseconds >= verticalDelayMs)
                        allowVertical = true;
                }
                if (targetSpeed == 0 && Math.Abs(backgroundSpeed) > 0.1)
                    allowVertical = true;

                foreach (var child in WindCanvas.Children)
                {
                    if (child is Line line)
                    {
                        double currentX = Canvas.GetLeft(line);
                        if (targetSpeed != 0)
                        {
                            if (!isDisplayNumberVisible)
                            {
                                double newX = currentX + (0 - currentX) * 0.1;
                                Canvas.SetLeft(line, newX);
                                if (allowVertical)
                                {
                                    double currentY = Canvas.GetTop(line);
                                    double newY = currentY + backgroundSpeed;
                                    if (newY > canvasHeight)
                                        newY -= canvasHeight;
                                    else if (newY < 0)
                                        newY = canvasHeight + newY;
                                    Canvas.SetTop(line, newY);
                                }
                            }
                            else
                            {
                                double newX = currentX + (offScreenX - currentX) * 0.1;
                                Canvas.SetLeft(line, newX);
                            }
                        }
                        else
                        {
                            if (Math.Abs(backgroundSpeed) > 0.1)
                            {
                                Canvas.SetLeft(line, 0);
                                double currentY = Canvas.GetTop(line);
                                double newY = currentY + backgroundSpeed;
                                if (newY > canvasHeight)
                                    newY -= canvasHeight;
                                else if (newY < 0)
                                    newY = canvasHeight + newY;
                                Canvas.SetTop(line, newY);
                            }
                            else
                            {
                                double newX = currentX + (offScreenX - currentX) * 0.1;
                                Canvas.SetLeft(line, newX);
                            }
                        }
                        line.Effect = (blurRadius > 0.1) ? new BlurEffect { Radius = blurRadius } : null;
                    }
                }

                if (targetSpeed == 0 && Math.Abs(backgroundSpeed) < 0.1 && !isDisplayNumberVisible)
                {
                    await AnimateNumberTextIn(token);
                }
                else if (targetSpeed != 0 && isDisplayNumberVisible)
                {
                    await AnimateNumberTextOut(token);
                }

                await Task.Delay(16, token);
            }
        }

        private async Task AnimateNumberTextIn(CancellationToken token)
        {
            double targetY = 50;
            double durationMs = 200;
            double startY = NumberTextTransform.Y;
            double startTime = Environment.TickCount;

            while (!token.IsCancellationRequested)
            {
                double elapsed = Environment.TickCount - startTime;
                double t = Math.Min(elapsed / durationMs, 1.0);
                NumberTextTransform.Y = startY + (targetY - startY) * t;
                if (t >= 1.0)
                    break;
                await Task.Delay(16, token);
            }
            isDisplayNumberVisible = true;
        }

        private async Task AnimateNumberTextOut(CancellationToken token)
        {
            double targetY = -100;
            double durationMs = 200;
            double startY = NumberTextTransform.Y;
            double startTime = Environment.TickCount;

            while (!token.IsCancellationRequested)
            {
                double elapsed = Environment.TickCount - startTime;
                double t = Math.Min(elapsed / durationMs, 1.0);
                NumberTextTransform.Y = startY + (targetY - startY) * t;
                if (t >= 1.0)
                    break;
                await Task.Delay(16, token);
            }
            isDisplayNumberVisible = false;
        }

        #endregion

        #region Tür-Blinking

        private async Task BlinkDoorsAsync(CancellationToken token)
        {
            var door1Color = Color.Parse("#e62d22");
            var door2Color = Color.Parse("#e30613");
            var door3Color = Color.Parse("#3aaa35");
            var blinkColor = Color.Parse("#808080");
            bool state = false;
            while (!token.IsCancellationRequested)
            {
                state = !state;
                foreach (var child in Door1Canvas.Children)
                {
                    if (child is Polygon poly)
                        poly.Fill = new SolidColorBrush(state ? blinkColor : door1Color);
                }
                foreach (var child in Door2Canvas.Children)
                {
                    if (child is Polygon poly)
                        poly.Fill = new SolidColorBrush(state ? blinkColor : door2Color);
                }
                foreach (var child in Door3Canvas.Children)
                {
                    if (child is Polygon poly)
                        poly.Fill = new SolidColorBrush(state ? blinkColor : door3Color);
                }
                await Task.Delay(1000, token);
            }
        }

        #endregion

        #region Blinkende Punkte (Dots)

        private async Task BlinkDotsAsync(CancellationToken token)
        {
            bool isOn = false;
            while (!token.IsCancellationRequested)
            {
                isOn = !isOn;
                var fillColor = isOn ? Colors.Green : Colors.DarkGray;
                var brush = new SolidColorBrush(fillColor);
                Dot1.Fill = brush;
                Dot2.Fill = brush;
                Dot3.Fill = brush;
                Dot4.Fill = brush;
                await Task.Delay(1000, token);
            }
        }

        #endregion

        #region Steuerung & Event-Handler

        private void CancelElevatorAnimation()
        {
            if (_ctsElevator != null)
            {
                _ctsElevator.Cancel();
                _ctsElevator.Dispose();
                _ctsElevator = null;
            }
        }

        private async void BtnUp_Click(object sender, RoutedEventArgs e)
        {
            CancelElevatorAnimation();
            targetSpeed = MaxSpeed;
            _ctsElevator = new CancellationTokenSource();
            try
            {
                await AnimateElevatorOffsetAsync(-50, 1000, _ctsElevator.Token);
            }
            catch (TaskCanceledException) { }
        }

        private async void BtnDown_Click(object sender, RoutedEventArgs e)
        {
            CancelElevatorAnimation();
            targetSpeed = -MaxSpeed;
            _ctsElevator = new CancellationTokenSource();
            try
            {
                await AnimateElevatorOffsetAsync(50, 1000, _ctsElevator.Token);
            }
            catch (TaskCanceledException) { }
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            CancelElevatorAnimation();
            targetSpeed = 0;
            _ctsElevator = new CancellationTokenSource();
            try
            {
                await AnimateElevatorToCenterAsync(1000, _ctsElevator.Token);
            }
            catch (TaskCanceledException) { }
        }
        #endregion
        private void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {

            var newWindowHome = new MainWindow();
            newWindowHome.Show();
            this.Close();

        }
        
    }
}
