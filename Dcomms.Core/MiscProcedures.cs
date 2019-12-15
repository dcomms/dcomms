using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
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
        public static void Initialize(DateTime compilationDateTimeUtc)
        {
            _compilationDateTimeUtc = compilationDateTimeUtc;
        }
        static DateTime _baseDateTime = new DateTime(2019, 01, 01);
        public static DateTime Uint32secondsToDateTime(uint seconds) //enough for 136 years
        {
            return _baseDateTime.AddSeconds(seconds);
        }
        public static uint DateTimeToUint32seconds(DateTime dt) //enough for 136 years
        {
            return (uint)(dt - _baseDateTime).TotalSeconds;
        }

        public static uint CompilationDateTimeUtc_uint32Seconds => DateTimeToUint32seconds(CompilationDateTimeUtc);


        public static DateTime Uint32minutesToDateTime(uint seconds) //enough for 8171 years
        {
            return _baseDateTime.AddMinutes(seconds);
        }
        public static uint DateTimeToUint32minutes(DateTime dt) //enough for 8171 years
        {
            return (uint)(dt - _baseDateTime).TotalMinutes;
        }


        public static DateTime Uint16daysToDateTime(ushort days) //enough for 179 years
        {
            return _baseDateTime.AddDays(days);
        }
        public static ushort DateTimeToUint16days(DateTime dt) //enough for 179 years
        {
            return (ushort)(dt - _baseDateTime).TotalDays;
        }

        public static Int64 DateTimeToInt64ticks(DateTime dt) 
        {
            return dt.Ticks;
        }
        public static DateTime Int64ticksToDateTime(Int64 ticks)
        {
            return new DateTime(ticks);
        }
        public static TimeSpan Int64ToTimeSpan(Int64 ticks)
        {
            return TimeSpan.FromTicks(ticks);
        }

        public static CultureInfo BandwidthToString_CultureInfo;
        public static string BandwidthToString(this float bandwidth, float? targetBandwidth = null) => BandwidthToString((float?)bandwidth, targetBandwidth);
        public static string BandwidthToString(this float? bandwidth, float? targetBandwidth = null)
        {
            if (bandwidth == null) return "";
            var sb = new StringBuilder();
            var cultureInfo = BandwidthToString_CultureInfo ?? CultureInfo.CurrentUICulture;
            if (bandwidth >= 1024 * 1024)
            {
                sb.AppendFormat(cultureInfo, "{0:F2}Mbps", bandwidth / (1024 * 1024));
            }
            else if (bandwidth >= 1024)
            {
                sb.AppendFormat(cultureInfo, "{0:F2}kbps", bandwidth / (1024));
            }
            else
                sb.AppendFormat(cultureInfo, "{0:F2}bps", bandwidth);

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
                new Tuple<float, Color>(0, Color.FromArgb(210, 255, 200)),
                new Tuple<float, Color>(TimeSpan.TicksPerMinute * 30.0f, Color.FromArgb(170, 255, 170)),
                new Tuple<float, Color>(TimeSpan.TicksPerDay * 2.0f, Color.FromArgb(170, 255, 140)),
                });

        }
        public static Color DowntimeDurationToColor(this TimeSpan duration)
        {
            return ValueToColor(duration.Ticks, new[] {
                new Tuple<float, Color>(0, Color.FromArgb(255, 240, 230)),
                new Tuple<float, Color>(5, Color.FromArgb(255, 220, 220)),
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


        public static unsafe bool EqualByteArrays(byte[] a1, byte[] a2)
        {
            if (a1 == a2) return true;
            if (a1 == null || a2 == null || a1.Length != a2.Length)
                return false;
            fixed (byte* p1 = a1, p2 = a2)
            {
                byte* x1 = p1, x2 = p2;
                int l = a1.Length;
                for (int i = 0; i < l / 8; i++, x1 += 8, x2 += 8)
                    if (*((long*)x1) != *((long*)x2)) return false;
                if ((l & 4) != 0) { if (*((int*)x1) != *((int*)x2)) return false; x1 += 4; x2 += 4; }
                if ((l & 2) != 0) { if (*((short*)x1) != *((short*)x2)) return false; x1 += 2; x2 += 2; }
                if ((l & 1) != 0) if (*((byte*)x1) != *((byte*)x2)) return false;
                return true;
            }
        }
        public static unsafe bool EqualFloatArrays(float[] a1, float[] a2)
        {
            if (a1 == a2) return true;
            if (a1 == null || a2 == null || a1.Length != a2.Length)
                return false;
            fixed (float* p1 = a1, p2 = a2)
            {
                float* x1 = p1, x2 = p2;
                int l = a1.Length;
                for (int i = 0; i < l; i++, x1++, x2++)
                    if (*x1 != *x2) return false;

                return true;
            }
        }
        public static unsafe int GetArrayHashCode(byte[] a)
        {
            fixed (byte* p = a)
            {
                byte* x = p;
                int l = a.Length;
                int r = 0;
                for (int i = 0; i < l / 4; i++, x += 4)
                    r ^= *((int*)x);
                return r;
            }
        }
        public static unsafe int GetArrayHashCode(float[] a)
        {
            fixed (float* p = a)
            {
                int* x = (int*)p;
                int l = a.Length;
                int r = 0;
                for (int i = 0; i < l; i++, x++)
                    r ^= *x;
                return r;
            }
        }
        public static string GetArrayHashCodeString(byte[] a) => String.Format("{0:X8}", GetArrayHashCode(a));

        public static unsafe bool EqualByteArrayHeader(byte[] header, byte[] array, int? ignoreByteAtOffset1)
        {
            if (header == array) return true;
            if (header == null || array == null || header.Length > array.Length)
                return false;

            if (ignoreByteAtOffset1.HasValue)
            {
                var ignoreByteAtOffset1Value = ignoreByteAtOffset1.Value;
                if (ignoreByteAtOffset1Value >= header.Length) throw new ArgumentException();
                fixed (byte* pHeader = header, pArray = array)
                {
                    return EqualByteArrayHeader2(pHeader, ignoreByteAtOffset1Value, pArray, ignoreByteAtOffset1Value) &&
                        EqualByteArrayHeader2(pHeader + ignoreByteAtOffset1Value + 1,
                            header.Length - ignoreByteAtOffset1Value - 1,
                            pArray + ignoreByteAtOffset1Value + 1,
                            array.Length - ignoreByteAtOffset1Value - 1);
                }
            }
            else
            {
                fixed (byte* pHeader = header, pArray = array)
                {
                    return EqualByteArrayHeader2(pHeader, header.Length, pArray, array.Length);                   
                }
            }
        }
        
        static unsafe bool EqualByteArrayHeader2(byte* pHeader, int headerLength, byte* pArray, int arrayLength)
        {
            if (headerLength > arrayLength)
                return false;    
               
            byte* x1 = pHeader, x2 = pArray;
            int l = headerLength;
            for (int i = 0; i < l / 8; i++, x1 += 8, x2 += 8)
                if (*((long*)x1) != *((long*)x2)) return false;
            if ((l & 4) != 0) { if (*((int*)x1) != *((int*)x2)) return false; x1 += 4; x2 += 4; }
            if ((l & 2) != 0) { if (*((short*)x1) != *((short*)x2)) return false; x1 += 2; x2 += 2; }
            if ((l & 1) != 0) if (*((byte*)x1) != *((byte*)x2)) return false;
            return true;
        }
        public static string ByteArrayToString(byte[] a)
        {
            return String.Join("", a.Select(x => x.ToString("X2")));
        }
        public static string ByteArrayToCsharpDeclaration(byte[] a)
        {
            return String.Join(", 0x", a.Select(x => x.ToString("X2")));
        }
        
        public static string VectorToString(double[] a)
        {
            return "[" + String.Join(", ", a.Select(x => x.ToString("0.000"))) + "]";
        }

        public static ushort? ToUShortNullable(this string str)
        {
            if (ushort.TryParse(str, out var r)) return r;
            return null;
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
