using System;
using System.Numerics;

namespace EllipticCurveCrypto
{
    public class EllipticCurve
    {
        public EllipticCurve(string name, BigInteger p, BigInteger a, BigInteger b, EllipticCurvePoint g, BigInteger n, short h, uint length)
        {
            Name = name;
            P = p;
            A = a;
            B = b;
            G = g;
            N = n;
            H = h;
            LengthInBits = length;
        }

        public string Name { get; }
        public BigInteger P { get; }
        public BigInteger A { get; }
        public BigInteger B { get; }
        public EllipticCurvePoint G { get; }
        public BigInteger N { get; }
        public short H { get; }

        /// <summary>
        /// Length in Bits
        /// </summary>
        public uint LengthInBits { get; }

        /// <summary>
        /// Returns True if the given point lies on the elliptic curve.
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        public bool IsPointOnCurve(EllipticCurvePoint point)
        {
            if (point == EllipticCurvePoint.InfinityPoint)
            {
                return true;
            }

            BigInteger y = point.Y;
            BigInteger x = point.X;
            BigInteger a = this.A;
            BigInteger b = this.B;
            BigInteger p = this.P;

            var rem = (y * y - x * x * x - a * x - b) % p;

            return rem == 0;
        }

        public void EnsureOnCurve(EllipticCurvePoint point)
        {

            if (!IsPointOnCurve(point))
            {
                throw new ArgumentException($"Point1 {point} not on curve");
            }
        }
    };
}
