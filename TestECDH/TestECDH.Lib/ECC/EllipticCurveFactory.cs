using System;
using System.Numerics;
using System.Globalization;

namespace EllipticCurveCrypto
{
    public static class EllipticCurveFactory
    {
        private static EllipticCurve GetEllipticCurveSecp256K1()
        {
            string name = EllipticCurveNames.Secp256K1;
            BigInteger p = BigInteger.Parse("00fffffffffffffffffffffffffffffffffffffffffffffffffffffffefffffc2f", NumberStyles.HexNumber);
            BigInteger a = 0;
            BigInteger b = 7;

            var gX = BigInteger.Parse("79be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798", NumberStyles.HexNumber);
            var gY = BigInteger.Parse("483ada7726a3c4655da4fbfc0e1108a8fd17b448a68554199c47d08ffb10d4b8", NumberStyles.HexNumber);
            EllipticCurvePoint g = new EllipticCurvePoint(gX, gY);

            BigInteger n = BigInteger.Parse("00fffffffffffffffffffffffffffffffebaaedce6af48a03bbfd25e8cd0364141", NumberStyles.HexNumber);
            Int16 h = 1;

            var secp256K1 = new EllipticCurve
            (
                name: name, p: p, a: a, b: b, g: g, n: n, h: h, length:256
            );

            return secp256K1;
        }

        private static EllipticCurve GetEllipticCurveSecp256R1()
        {
            string name = EllipticCurveNames.Secp256R1;
            BigInteger p = BigInteger.Parse("00FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF", NumberStyles.HexNumber);
            BigInteger a = BigInteger.Parse("00FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFC", NumberStyles.HexNumber);
            BigInteger b = BigInteger.Parse("005AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B", NumberStyles.HexNumber);

            var gX = BigInteger.Parse("006B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296", NumberStyles.HexNumber);
            var gY = BigInteger.Parse("004FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5", NumberStyles.HexNumber);
            EllipticCurvePoint g = new EllipticCurvePoint(gX, gY);

            BigInteger n = BigInteger.Parse("00FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551", NumberStyles.HexNumber);
            Int16 h = 1;

            var secp256R1 = new EllipticCurve
            (
                name: name, p: p, a: a, b: b, g: g, n: n, h: h, length: 256
            );

            return secp256R1;
        }

        public static EllipticCurve Create(string curveName)
        {
            if (curveName == EllipticCurveNames.Secp256K1)
            {
                return GetEllipticCurveSecp256K1();
            }

            if (curveName == EllipticCurveNames.Secp256R1)
            {
                return GetEllipticCurveSecp256R1();
            }

            throw new NotSupportedException($"{curveName} not supported");
        }
    }
}
