using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DSP
{
    /// <summary>
    /// simplest IIR filter, used to calculate recent bandwidth, packets per second, etc
    /// MT-unsafe
    /// </summary>
    public class IirFilterCounter
    {
        readonly double _decayTimeTicksInv;
        readonly double _unitToDecayTime;
        public IirFilterCounter(double decayTimeTicks, double unitTimeTicks)
        {
            _decayTimeTicksInv = 1.0 / decayTimeTicks;
            _unitToDecayTime = unitTimeTicks * _decayTimeTicksInv;
        }
        public void Input(double value, double timePassedTicks)
        {
            OnTimePassed(timePassedTicks);
            Input(value);
        }
        public void Input(double value)
        {
            _s += value;
        }

        uint? _latestTimeObserved;
        /// <summary>
        /// if this procedure is used - "OnTimePassed" must not be called externally
        /// </summary>
        public void OnTimeObserved(uint timeNow32)
        {
            if (_latestTimeObserved.HasValue)
            {
                if (MiscProcedures.TimeStamp1IsLess(timeNow32, _latestTimeObserved.Value))
                {
                    // can happen if 2 threads use this class instance in parallel, and each thread calls this procedure.  it is normal situation, when "timeNow32" is a 'little bit' less than "_latestTimeObserved"
                    //  todo event for developer  in case of "high" jumps?
                    return;
                }
                               
                OnTimePassed(unchecked(timeNow32 - _latestTimeObserved.Value));
            }
            _latestTimeObserved = timeNow32;
        }

        public void OnTimePassed(double timePassedTicks)
        {
            if (timePassedTicks < 0) throw new ArgumentException(nameof(timePassedTicks));
            var a = timePassedTicks * _decayTimeTicksInv;
            DecayProcedure(a, ref _s);
        }
        double _s;
        public float OutputPerUnit // bits per [second = unit]
        {
            get
            {
                return (float)(_s * _unitToDecayTime);
            }
            set
            {
                _s = value / _unitToDecayTime;
            }
        }

        internal static void DecayProcedure(double a, ref double s) // common procedure
        {
            const double maxDecayA = 0.3;
        _loop:
            if (a <= maxDecayA)
            {
                s *= (1.0 - a);
            }
            else if (a > 10)
            {
                s = 0;
            }
            else
            {
                s *= (1.0 - maxDecayA);
                a -= maxDecayA;
                goto _loop;
            }
        }
    }

    
    /// <summary>
    /// is used to calculate recent average packet loss
    /// MT-unsafe
    /// </summary>
    public class IirFilterAverage
    {
        readonly double _decayTimeTicksInv;
        public IirFilterAverage(double decayTimeTicks)
        {
            _decayTimeTicksInv = 1.0 / decayTimeTicks;
        }

        public void Input(double value, double timePassedTicks)
        {
            Input(value);
            OnTimePassed(timePassedTicks);
        }
        public void Input(double value)
        {
            _s += value;
            _s_ref += 1.0;
        }
        public void OnTimePassed(double timePassedTicks)
        {
            var a = timePassedTicks * _decayTimeTicksInv;
            IirFilterCounter.DecayProcedure(a, ref _s);
            IirFilterCounter.DecayProcedure(a, ref _s_ref);

        }
        double _s;
        double _s_ref;
        public float Output => (float)(_s / _s_ref); // packet loss



        uint? _latestTimeObserved;
        /// <summary>
        /// if this procedure is used - "OnTimePassed" must not be called externally
        /// </summary>
        public void OnTimeObserved(uint timeNow32)
        {
            if (_latestTimeObserved.HasValue)
            {
                if (MiscProcedures.TimeStamp1IsLess(timeNow32, _latestTimeObserved.Value))
                {
                    ////   throw new ArgumentException(nameof(timeNow32));
                    // can happen if 2 threads use this class instance in parallel, and each thread calls this procedure.  it is normal situation, when "timeNow32" is a 'little bit' less than "_latestTimeObserved"
                    //  todo event for developer  in case of "high" jumps?
                    return;
                }
                OnTimePassed(unchecked(timeNow32 - _latestTimeObserved.Value));
            }
            _latestTimeObserved = timeNow32;
        }


    }

}
