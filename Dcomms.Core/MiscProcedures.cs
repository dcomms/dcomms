using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace Dcomms
{
    public static class MiscProcedures
    {
        static DateTime? _compilationDateTimeUtc;
        public static void AssertIsInitialized()
        {
            if (!_compilationDateTimeUtc.HasValue) throw new InvalidOperationException("Dcomms module is not initialized");            
        }
        public static DateTime MinPeerCompilationDateTimeUtc = DateTime.MinValue;
        public static DateTime CompilationDateTimeUtc
        {
            get
            {
                AssertIsInitialized();
                return _compilationDateTimeUtc.Value;
            }
        }
        public static void Initialize(DateTime compilationDateTimeUtc, DateTime minPeerCompilationDateTimeUtc)
        {
            MinPeerCompilationDateTimeUtc = minPeerCompilationDateTimeUtc;
            _compilationDateTimeUtc = compilationDateTimeUtc;
        }
        public static DateTime ToDateTime(uint seconds)
        {
            return new DateTime(2019, 01, 01).AddSeconds(seconds);
        }
        public static uint CompilationDateTimeUtc_uint32 => (uint)(CompilationDateTimeUtc - new DateTime(2019, 01, 01)).TotalSeconds;

        public static string BandwidthToString(this float bandwidth, float? targetBandwidth = null) => BandwidthToString((float?)bandwidth, targetBandwidth);
        public static string BandwidthToString(this float? bandwidth, float? targetBandwidth = null)
        {
            if (bandwidth == null) return "";
            var sb = new StringBuilder();
            if (bandwidth >= 1024 * 1024)
            {
                sb.AppendFormat("{0:0.00}Mbps", bandwidth / (1024 * 1024));
            }
            else if (bandwidth >= 1024)
            {
                sb.AppendFormat("{0:0.00}kbps", bandwidth / (1024));
            }
            else
                sb.AppendFormat("{0:0.00}bps", bandwidth);

            if (targetBandwidth.HasValue)
            {
                sb.Append("/");
                sb.Append(targetBandwidth.BandwidthToString());
            }

            return sb.ToString();
        }
        public static string PpsToString(this float packetsPerSecond)
        {
            var sb = new StringBuilder();
            if (packetsPerSecond > 1024 * 1024)
            {
                sb.AppendFormat("{0:0.00}Mpps", packetsPerSecond / (1024 * 1024));
            }
            else if (packetsPerSecond > 1024)
            {
                sb.AppendFormat("{0:0.00}kpps", packetsPerSecond / (1024));
            }
            else
                sb.AppendFormat("{0:0.00}pps", packetsPerSecond);

            return sb.ToString();
        }
        public static string PacketLossToString(this float loss)
        {
            return String.Format("{0:0.00}%", loss * 100);
        }
               
        public static bool TimeStamp1IsLess(uint t1, uint t2)
        {
            //    t1              t2         result
            //   ffff ffff       ffff fffe   false
            //   0000 0000       ffff ffff   false
            //   ffff ffff       0000 0000   true
            //   0000 0001       0000 0000   false

            if (t1 > 0xCFFFFFFF && t2 < 0x3FFFFFFF)
                return true;
            if (t1 < 0x3FFFFFFF && t2 > 0xCFFFFFFF)
                return false;

            return t1 < t2;
        }
        public static string TimeSpanToString(this TimeSpan? ts)
        {
            if (ts == null) return "N/A";
            if (ts.Value.Ticks < TimeSpan.TicksPerSecond) return String.Format("{0:0.0}ms", ts.Value.TotalMilliseconds);
            else if (ts.Value.Ticks < TimeSpan.TicksPerMinute) return String.Format("{0:0.0}s", ts.Value.TotalSeconds);
            else if (ts.Value.Ticks < TimeSpan.TicksPerHour) return String.Format("{0:0.0}m", ts.Value.TotalMinutes);
            else return String.Format("{0:0.0}h", ts.Value.TotalHours);
        }        
        public static string TimeSpanToStringHMS(this TimeSpan ts)
        {
            var r = new StringBuilder();
            var d = (int)Math.Floor(ts.TotalDays);
            if (d != 0)
                r.AppendFormat("{0}d ", d);

            if (r.Length != 0 || ts.Hours != 0)
                r.AppendFormat("{0}h ", ts.Hours);

            if (r.Length != 0 || ts.Minutes != 0)
                r.AppendFormat("{0}m ", ts.Minutes);

            r.AppendFormat("{0}s", ts.Seconds);

            return r.ToString();
        }

        public static Color RttToColor(this TimeSpan? rtt)
        {
            if (!rtt.HasValue) return Color.Transparent;
            return ValueToColor((float)rtt.Value.TotalMilliseconds, new[] {
                new Tuple<float, Color>(0, Color.FromArgb(100, 255, 100)),
                new Tuple<float, Color>(200, Color.FromArgb(255, 255, 150)),
                new Tuple<float, Color>(1000, Color.FromArgb(255, 150, 150))
                });
        }

        public static Color BandwidthToColor(this float bandwidth, float? targetBandwidth = null)
        {
            if (targetBandwidth == null)
            {
                return ValueToColor(bandwidth, new[] {
                    new Tuple<float, Color>(0, Color.FromArgb(255, 150, 150)),
                    new Tuple<float, Color>(1024 * 1024, Color.FromArgb(255, 255, 100)),
                    new Tuple<float, Color>(20 * 1024 * 1024, Color.FromArgb(100, 255, 100)),
                    new Tuple<float, Color>(100 * 1024 * 1024, Color.FromArgb(100, 255, 200))
                });
            }
            else
            {
                var ratio = bandwidth / targetBandwidth.Value;
                return ValueToColor(ratio, new[] {
                    new Tuple<float, Color>(0, Color.FromArgb(255, 150, 150)),
                    new Tuple<float, Color>(0.5f, Color.FromArgb(255, 255, 100)),
                    new Tuple<float, Color>(1.0f, Color.FromArgb(100, 255, 100)),
                    new Tuple<float, Color>(1.5f, Color.FromArgb(100, 255, 200))
                });
            }
        }
        public static Color PacketLossToColor(this float? packetLoss01)
        {
            if (!packetLoss01.HasValue) return Color.Transparent;
            return ValueToColor(packetLoss01.Value, new[] {
                new Tuple<float, Color>(0, Color.FromArgb(100, 255, 100)),
                new Tuple<float, Color>(0.02f, Color.FromArgb(255, 255, 0)),
                new Tuple<float, Color>(0.1f, Color.FromArgb(255, 150, 150)),
                });
        }
        public static Color PacketLossToColor_UBw(this float? packetLoss01)
        {
            if (!packetLoss01.HasValue) return Color.Transparent;
            return ValueToColor(packetLoss01.Value, new[] {
                new Tuple<float, Color>(0, Color.FromArgb(0, 100, 0)),
                new Tuple<float, Color>(0.05f, Color.FromArgb(50, 50, 0)),
                new Tuple<float, Color>(0.3f, Color.FromArgb(150, 50, 0)),
                });
        }

        public static Color UptimeDurationToColor(this TimeSpan duration)
        {
            return ValueToColor(duration.Ticks, new[] {
                new Tuple<float, Color>(0, Color.FromArgb(220, 255, 200)),
                new Tuple<float, Color>(TimeSpan.TicksPerMinute * 30.0f, Color.FromArgb(180, 255, 180)),
                new Tuple<float, Color>(TimeSpan.TicksPerDay * 2.0f, Color.FromArgb(180, 255, 150)),
                });

        }
        public static Color DowntimeDurationToColor(this TimeSpan duration)
        {
            return ValueToColor(duration.Ticks, new[] {
                new Tuple<float, Color>(0, Color.FromArgb(255, 220, 220)),
                new Tuple<float, Color>(TimeSpan.TicksPerSecond * 10.0f, Color.FromArgb(255, 180, 180)),
                new Tuple<float, Color>(TimeSpan.TicksPerMinute * 1.0f, Color.FromArgb(255, 120, 120)),
                new Tuple<float, Color>(TimeSpan.TicksPerMinute * 10.0f, Color.FromArgb(255, 50, 50)),
                new Tuple<float, Color>(TimeSpan.TicksPerMinute * 60.0f, Color.FromArgb(200, 0, 0)),
                });

        }

        /// <param name="referencePoints">must be sorted by value, ascending</param>
        public static Color ValueToColor(this float value, Tuple<float,Color>[] referencePoints)
        {
            var p1 = referencePoints[0];
            if (value < p1.Item1) return p1.Item2;
            for (int i = 1; i < referencePoints.Length; i++)
            {
                var p2 = referencePoints[i];
                if (value < p2.Item1)
                {
                    var v01 = (value - p1.Item1) / (p2.Item1 - p1.Item1);
                    if (v01 < 0) v01 = 0; else if (v01 > 1) v01 = 1;
                    return Color.FromArgb(ColorComponentSubroutine(v01, p1.Item2.A, p2.Item2.A), ColorComponentSubroutine(v01, p1.Item2.R, p2.Item2.R), ColorComponentSubroutine(v01, p1.Item2.G, p2.Item2.G), ColorComponentSubroutine(v01, p1.Item2.B, p2.Item2.B));
                }
                p1 = p2;
            }
            return p1.Item2;
        }
        static int ColorComponentSubroutine(float v01, int c1, int c2)
        {
            return c1 + (int)(v01 * (c2 - c1));
        }
    }
    public class AverageSingle
    {
        uint _n;
        float _sum;
        public float? Average => _n != 0 ? (float?)(_sum / _n) : null;
        public void Input(float v)
        {
            if (float.IsNaN(v)) return;
            if (float.IsInfinity(v)) return;
            _sum += v;
            _n++;
        }
    }
}
