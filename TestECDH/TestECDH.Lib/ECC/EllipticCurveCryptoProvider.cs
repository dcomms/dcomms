using System;
using System.Linq;
using System.Numerics;


namespace EllipticCurveCrypto
{
    public class EllipticCurveCryptoProvider
    {
        Random _rnd = new Random();
        /// <summary>
        /// </summary>
        /// <param name="curveName"></param>
        public EllipticCurveCryptoProvider(string curveName)
        {
            _curves = EllipticCurveFactory.Create(curveName);
            _calculator =new EllipticCurveCalculator(_curves);
        }

        readonly EllipticCurve _curves;
        readonly EllipticCurveCalculator _calculator;

        public void MakeKeyPair(out BigInteger privateKey, out EllipticCurvePoint publicKey)
        {
            var curve = _curves;
            var keyBytes = curve.LengthInBits / 8;

            var unsignedBytes = new byte[] { 0x00 };
            var randomBytes = new byte[keyBytes]; _rnd.NextBytes(randomBytes); // todo not secure  CryptographicBuffer.GenerateRandom(keyBytes).ToArray();
            var positiveRandomBytes = randomBytes.Concat(unsignedBytes).ToArray(); // To avoid randomBytes treated as negtive value

            var randomValue = new BigInteger(positiveRandomBytes);

            do
            {
                privateKey = (randomValue % curve.N);
            }
            while (privateKey == 0);


            publicKey = _calculator.ScalarMult(privateKey, curve.G);
        }

        public EllipticCurvePoint DeriveSharedSecret(BigInteger myPrivateKey, EllipticCurvePoint otherPartyPublicKey)
        {
            var sharedSecret = _calculator.ScalarMult(myPrivateKey, otherPartyPublicKey);
            return sharedSecret;
        }

        /// <summary>
        /// Copy right-most outputBytesLength bytes from inputBytes to outputBytes
        /// </summary>
        /// <param name="inputBytes"></param>
        /// <param name="outputBytesLength"></param>
        /// <returns></returns>
        private byte[] CopyReversedBytesToBytes(byte[] inputBytes, int outputBytesLength)
        {
            var outputBytes = new byte[outputBytesLength];

            int outputBytesindex = outputBytesLength - 1, indexInputBytes = 0;

            while ( outputBytesindex > -1  )
            {
                if (indexInputBytes < inputBytes.Length)
                {
                    outputBytes[outputBytesindex] = inputBytes[indexInputBytes];
                }
                else
                {
                    outputBytes[outputBytesindex] = 0x00;
                }

                --outputBytesindex;
                ++indexInputBytes;
            }

            return outputBytes;
        }

        //public IBuffer DerivteKeyWithHkdf(EllipticCurvePoint sharedSecret, IBuffer salt, IBuffer info)
        //{
        //    var curve = _curves;
        //    int ikmLengthInBytes = (int)curve.LengthInBits / 8;

        //    var xBytes = sharedSecret.X.ToByteArray();

        //    // Convert to from Little-endian to Big-endian
        //    byte[] ikmBytes = CopyReversedBytesToBytes(xBytes, ikmLengthInBytes);
        //    IBuffer inpitKeyMaterial = CryptographicBuffer.CreateFromByteArray(ikmBytes);

        //    var hkdf = new HMacBasedExtractAndExpandKeyDerivationFunction();
        //    // ReSharper disable once ExpressionIsAlwaysNull
        //    IBuffer outputKeyMaterial = hkdf.DeriveKey(MacAlgorithmNames.HmacSha256, salt, info, inpitKeyMaterial);

        //    return outputKeyMaterial;
        //}
    }
}
