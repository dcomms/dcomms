using EllipticCurveCrypto;
using System;
using System.Collections.Generic;
using System.Text;

namespace TestECDH.Lib
{
    class Test2
    {
        readonly Action<string> _wtl;
        public Test2(Action<string> wtl)
        {
            _wtl = wtl;
        }
        public void Test2_1()
        {
            int n = 10000;
            _wtl("ECDH deriving shared key...");
            using (var serverECDH = new EllipticCurveCryptoProvider(EllipticCurveNames.Secp256K1))
            using (var clientECDH = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256))
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < n; i++)
                {
                    byte[] serverSharedKey = serverECDH.DeriveKeyMaterial(clientECDH.PublicKey);
                }
                sw.Stop();
                _wtl($"{(double)n / sw.Elapsed.TotalSeconds} ECDH shared key derivations per second");
            }
        }

    }
}
