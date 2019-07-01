using System;
using System.Numerics;

namespace EllipticCurveCrypto
{
    public class EllipticCurveCalculator
    {
        public EllipticCurveCalculator(EllipticCurve curve)
        {
            _curve = curve;
        }

        private EllipticCurve _curve;

        public BigInteger InverseMod(BigInteger k, BigInteger p)
        {
            if (k ==0)
            {
                throw new DivideByZeroException("k");
            }

            if (k < 0)
            {
                return p - InverseMod(-k, p);
            }

            BigInteger s = 0, oldS = 1;
            BigInteger r = p, oldR = k;

            while (r != 0)
            {
                BigInteger quotient = oldR / r;

                BigInteger prov = r;
                r = oldR - quotient * prov;
                oldR = prov;

                prov = s;
                s = oldS - quotient * prov;
                oldS = prov;
            }

            BigInteger gcd = oldR;
            BigInteger x = oldS;

            if (gcd != 1)
            {
                throw new NotSupportedException($"gcd != 1, {gcd}");
            }


            BigInteger modularKxp = Modular(k*x, p);
            if (modularKxp != 1)
            {
                throw new NotSupportedException($"(k * x) % p != 1, {modularKxp}");
            }

            var result = Modular(x, p);

            return result;
        }

        public EllipticCurvePoint Add(EllipticCurvePoint point1, EllipticCurvePoint point2)
        {
            _curve.EnsureOnCurve(point1);
            _curve.EnsureOnCurve(point2);

            if (EllipticCurvePoint.IsInfinityPoint(point1))
            {
                return point2;
            }

            if (EllipticCurvePoint.IsInfinityPoint(point2))
            {
                return point1;
            }

            var x1 = point1.X;
            var y1 = point1.Y;
            var x2 = point2.X;
            var y2 = point2.Y;
            var p = _curve.P;
            var a = _curve.A;

            BigInteger m;

            if (x1 == x2)
            {
                if (y1 != y2) // point1 + (-point1) = 0
                {
                    return EllipticCurvePoint.InfinityPoint;
                }
               
                // This is the case point1 == point2.
                m = (3 * x1 * x1 + a) * InverseMod(2 * y1, p);
            }
            else // point1 != point2.
            {
                m = (y1 - y2) * InverseMod(x1 - x2, p);
            }

            BigInteger x3 = m * m - x1 - x2;
            BigInteger y3 = y1 + m * (x3 - x1);
            var result = new EllipticCurvePoint(Modular(x3, p), Modular(-y3, p));

            return result;
        }

        public EllipticCurvePoint PointNeg(EllipticCurvePoint point)
        {
            _curve.EnsureOnCurve(point);

            // -0 = 0
            if (EllipticCurvePoint.IsInfinityPoint(point))
            {
                return point;
            }

            var x = point.X;
            var y = point.Y;
            var p = _curve.P;
            var result = new EllipticCurvePoint(x, Modular(-y, p));

            _curve.EnsureOnCurve(result);
            return result;
        }

        public EllipticCurvePoint ScalarMult(BigInteger k, EllipticCurvePoint point)
        {
            _curve.EnsureOnCurve(point);

            var n = _curve.N;

            if (k % n == 0 || EllipticCurvePoint.IsInfinityPoint(point))
            {
                return EllipticCurvePoint.InfinityPoint;
            }

            // k * point = -k * (-point)
            if (k < 0)
                return ScalarMult(-k, PointNeg(point));

            EllipticCurvePoint result = EllipticCurvePoint.InfinityPoint;
            EllipticCurvePoint addend = point;

            while (k != 0)
            {
                if ((k & 1) == 1)
                {
                    result = Add(result, addend);
                }

                addend = Add(addend, addend);

                k >>= 1;
            }

            _curve.EnsureOnCurve(result);
            return result;
        }

        public BigInteger Modular(BigInteger k, BigInteger p)
        {
            BigInteger reminder = k % p;

            if (reminder < 0)
                reminder += p;

            return reminder;
        }
    }
}
