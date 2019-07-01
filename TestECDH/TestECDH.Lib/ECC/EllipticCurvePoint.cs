using System.Numerics;

namespace EllipticCurveCrypto
{
    public class EllipticCurvePoint
    {
        public EllipticCurvePoint(BigInteger x, BigInteger y)
        {
            X = x;
            Y = y;
        }
        public BigInteger X { get; }
        public BigInteger Y { get; }
        public static EllipticCurvePoint InfinityPoint => null;
        public static bool IsInfinityPoint(EllipticCurvePoint point)
        {
            return point == EllipticCurvePoint.InfinityPoint;
        }
    }
}
