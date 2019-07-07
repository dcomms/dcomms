using Cryptography.ECDSA;
using EllipticCurveCrypto;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
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

        public void TestSHA256()
        {
            int TestSha256inputSize  = 128;

            var sw = Stopwatch.StartNew();
            var input = new byte[TestSha256inputSize];
            var rnd = new Random();
            rnd.NextBytes(input);
            int n = 1000000;
            for (int i = 0; i < n; i++)
            {
                var hash = Sha256Manager.GetHash(input);
            }
            sw.Stop();
            var callsPerSec = (double)n / sw.Elapsed.TotalSeconds;
            _wtl($"SHA256 calls per sec: {callsPerSec}");
        }

        public void TestECDSA_Sign()
        {
            var key = Secp256K1Manager.GenerateRandomKey();
            var sw = new Stopwatch();
            var rand = new RNGCryptoServiceProvider();
            byte[] msg = new byte[64];
            int n = 5000;
            sw.Start();
            for (int i = 0; i < n; i++)
            {
                rand.GetBytes(msg);
                var hash = Sha256Manager.GetHash(msg);
                var signature1 = Secp256K1Manager.SignCompressedCompact(hash, key);
              //  Assert(signature1.Length == 65);
              //  Assert(Secp256K1Manager.IsCanonical(signature1, 1));
             //   if (!Secp256K1Manager.IsCanonical(signature1, 1))
             //   {
             //       _wtl($"signature1 not canonical - skip [{i}]");
            //    }
            }

            sw.Stop();
            var callsPerSec = (double)n / sw.Elapsed.TotalSeconds;
            _wtl($"Secp256K1Manager.SignCompressedCompact calls per sec: {callsPerSec}");
        }
        public void TestECDSA_Verify()
        {
            var key = Secp256K1Manager.GenerateRandomKey();
            var sw = new Stopwatch();
            var rand = new RNGCryptoServiceProvider();
            byte[] msg = new byte[64];
                rand.GetBytes(msg);
                var hash = Sha256Manager.GetHash(msg);
                var signature1 = Secp256K1Manager.SignCompressedCompact(hash, key);
            int n = 5000;
            sw.Start();
            for (int i = 0; i < n; i++)
            {
                //  Assert(signature1.Length == 65);
                  Assert(Secp256K1Manager.IsCanonical(signature1, 1));
                //   if (!Secp256K1Manager.IsCanonical(signature1, 1))
                //   {
                //       _wtl($"signature1 not canonical - skip [{i}]");
                //    }
            }

            sw.Stop();
            var callsPerSec = (double)n / sw.Elapsed.TotalSeconds;
            _wtl($"Secp256K1Manager.SignCompressedCompact calls per sec: {callsPerSec}");
        }
        void Assert(bool b)
        {
            if (!b) throw new Exception();
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
