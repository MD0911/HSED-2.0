using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSED_2._0
{
     sealed class PositionInterpolator
    {
        private readonly int _frameMs;
        private readonly int _maxLagMs;
        private readonly int _minExtraSteps;
        private readonly int _maxExtraSteps;
        private readonly double _smallDeltaPx;
        private readonly double _largeDeltaPx;

        private readonly Queue<double> _queue = new();
        private double _current;

        public PositionInterpolator(
            int frameMs = 50,         // 20 FPS
            int maxLagMs = 250,       // maximaler Puffer, darüber werden Frames verworfen
            int minExtraSteps = 1,    // bei merkbaren Sprüngen mindestens 1 bis 3 Zwischenschritte
            int maxExtraSteps = 15,   // Obergrenze für sehr große Sprünge
            double smallDeltaPx = 2,  // unterhalb davon keine Zwischenwerte nötig
            double largeDeltaPx = 200 // ab hier volle Zwischenwertzahl
        )
        {
            _frameMs = frameMs;
            _maxLagMs = maxLagMs;
            _minExtraSteps = minExtraSteps;
            _maxExtraSteps = maxExtraSteps;
            _smallDeltaPx = smallDeltaPx;
            _largeDeltaPx = largeDeltaPx;
        }

        public void Reset(double value)
        {
            _queue.Clear();
            _current = value;
        }

        public void Enqueue(double next)
        {
            // nur nachrechnen, nie vorrechnen
            double delta = Math.Abs(next - _current);

            int steps = ComputeSteps(delta);
            if (steps <= 0)
            {
                // direkter Sprung zulässig, wenn der Abstand winzig ist
                _queue.Enqueue(next);
            }
            else
            {
                // gleichmäßig verteilte Zwischenwerte + das Ziel selbst
                for (int i = 1; i <= steps; i++)
                {
                    double t = (double)i / (steps + 1);
                    _queue.Enqueue(_current + (next - _current) * t);
                }
                _queue.Enqueue(next);
            }

            // Lag hart begrenzen, damit wir nicht "hinterherhängen"
            int maxQueued = Math.Max(1, _maxLagMs / _frameMs);
            while (_queue.Count > maxQueued)
                _queue.Dequeue();
        }

        public bool TryDequeue(out double value)
        {
            if (_queue.Count > 0)
            {
                _current = _queue.Dequeue();
                value = _current;
                return true;
            }
            value = _current;
            return false;
        }

        private int ComputeSteps(double delta)
        {
            if (delta <= _smallDeltaPx) return 0;
            if (delta >= _largeDeltaPx) return _maxExtraSteps;

            double t = (delta - _smallDeltaPx) / (_largeDeltaPx - _smallDeltaPx);
            int s = _minExtraSteps + (int)Math.Round(t * (_maxExtraSteps - _minExtraSteps));
            return s;
        }
    }

}
