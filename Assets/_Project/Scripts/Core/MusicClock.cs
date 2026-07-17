namespace Robogame.Core
{
    /// <summary>
    /// Pure offset estimator between two same-rate audio clocks — the
    /// two-clocks bridge (ADR-0007). FMOD mixes on its own DSP clock;
    /// Unity stingers are scheduled on <c>AudioSettings.dspTime</c>.
    /// Both clocks advance at the hardware sample rate, so their offset
    /// is constant apart from staircase jitter of up to one mixer block
    /// (~21 ms) per main-thread read. Feed one
    /// (<paramref name="sourceSeconds"/>, <paramref name="targetSeconds"/>)
    /// pair per frame; <see cref="ToTarget"/> then maps FMOD track time
    /// into the dsp-time domain the beat grid lives in.
    /// </summary>
    /// <remarks>
    /// Estimator shape: mean over the warmup window, then an
    /// exponential moving average whose per-sample correction is
    /// clamped. The clamp is load-bearing — a single wild read (editor
    /// hiccup, device change glitch) must nudge the grid by
    /// milliseconds, not yank every scheduled stinger audibly off-beat.
    /// Unity-free and allocation-free (INV-6); covered by
    /// <c>MusicClockTests</c>.
    /// </remarks>
    public sealed class MusicClock
    {
        /// <summary>Samples required before the estimate is usable.</summary>
        public const int WarmupSamples = 8;

        private const double Alpha = 0.05;            // EMA weight per sample
        private const double MaxStepSeconds = 0.005;  // innovation clamp

        private int _count;
        private double _warmupSum;
        private double _offset;

        /// <summary>True once enough samples have been accumulated.</summary>
        public bool Ready => _count >= WarmupSamples;

        /// <summary>Current estimate of (target − source); 0 before the first sample.</summary>
        public double OffsetSeconds => _offset;

        public void AddSample(double sourceSeconds, double targetSeconds)
        {
            double observed = targetSeconds - sourceSeconds;
            if (_count < WarmupSamples)
            {
                _warmupSum += observed;
                _count++;
                _offset = _warmupSum / _count;
                return;
            }

            double step = Alpha * (observed - _offset);
            if (step > MaxStepSeconds) step = MaxStepSeconds;
            else if (step < -MaxStepSeconds) step = -MaxStepSeconds;
            _offset += step;
        }

        /// <summary>Map a source-clock time into the target clock's domain.</summary>
        public double ToTarget(double sourceSeconds) => sourceSeconds + _offset;

        public void Reset()
        {
            _count = 0;
            _warmupSum = 0;
            _offset = 0;
        }
    }
}
