using EllipticCurveCrypto;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace TestECDH.Lib
{
    public class Test2
    {
        readonly Action<string> _wtl;
        public Test2(Action<string> wtl)
        {
            _wtl = wtl;
        }
        public void Test2_1()
        {
            var p = new EllipticCurveCryptoProvider(EllipticCurveNames.Secp256K1);
                       
            int n = 100;
            _wtl("creating key pairs...");
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < n; i++)
                {
                    p.MakeKeyPair(out var privateKey, out var publicKey);
                }
                sw.Stop();
                _wtl($"{(double)n / sw.Elapsed.TotalSeconds} keypairs per second");
            }

            //int n = 10000;
            //_wtl("ECDH deriving shared key...");
            //using (var serverECDH = new EllipticCurveCryptoProvider(EllipticCurveNames.Secp256K1))
            //using (var clientECDH = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256))
            //{
            //    var sw = Stopwatch.StartNew();
            //    for (int i = 0; i < n; i++)
            //    {
            //        byte[] serverSharedKey = serverECDH.DeriveKeyMaterial(clientECDH.PublicKey);
            //    }
            //    sw.Stop();
            //    _wtl($"{(double)n / sw.Elapsed.TotalSeconds} ECDH shared key derivations per second");
            //}
        }
        
        public void Test2_2()
        {
            var p = new EllipticCurveCryptoProvider(EllipticCurveNames.Secp256K1);

            int n = 2000;
            _wtl("deriving shared keys...");

            var sw = Stopwatch.StartNew();
            p.MakeKeyPair(out var privateKeyC, out var publicKeyC);
            p.MakeKeyPair(out var privateKeyS, out var publicKeyS);
            for (int i = 0; i < n; i++)
            {
                p.DeriveSharedSecret(privateKeyS, publicKeyC);
            }
            sw.Stop();
            _wtl($"{(double)n / sw.Elapsed.TotalSeconds} shared keys derived per second");
        }
    }
}
