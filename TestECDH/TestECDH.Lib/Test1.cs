using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TestECDH.Lib
{

    public class Test1
    {
        readonly Action<string> _wtl;
        public Test1(Action<string> wtl)
        {
            _wtl = wtl;
        }
        public void Test1_1()
        {
            //new ECParameters
            //{
            //    Curve = ECCurve.NamedCurves.nistP256,
            //    D = xxx
            //};
            using (var aliceECDH = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256))
            using (var bobECDH = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256))
            {
                _wtl($"alice public key: {Encoding.ASCII.GetString(aliceECDH.PublicKey.ToByteArray())}");
                _wtl($"alice private key: {Encoding.ASCII.GetString(aliceECDH.ExportECPrivateKey())}");
                _wtl($"bob public key: {Encoding.ASCII.GetString(bobECDH.PublicKey.ToByteArray())}");
                _wtl($"bob private key: {Encoding.ASCII.GetString(bobECDH.ExportECPrivateKey())}");
                // alice.KeyDerivationFunction = ECDiffieHellmanKeyDerivationFunction.Hash;
                // alice.HashAlgorithm = CngAlgorithm.Sha256;
                // alicePublicKey = alice.PublicKey.ToByteArray();


                byte[] aliceSharedKey = aliceECDH.DeriveKeyMaterial(bobECDH.PublicKey);
                byte[] bobSharedKey = bobECDH.DeriveKeyMaterial(aliceECDH.PublicKey);
                _wtl($"alice shared key: {Encoding.ASCII.GetString(aliceSharedKey)}");
                _wtl($"bob shared key: {Encoding.ASCII.GetString(bobSharedKey)}");

                Send(aliceSharedKey, "Secret message", out var encryptedMessage, out var iv);
                var decoded = Receive(bobSharedKey, encryptedMessage, iv);
                _wtl($"decoded: {decoded}");
            }
        }
        public void Test1_2()
        {
            int n = 1000;
            _wtl("ECDH keys generation...");
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++)
            {
                using (var serverECDH = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256))
                {

                }
            }
            sw.Stop();
            _wtl($"{(double)n / sw.Elapsed.TotalSeconds} ECDH keypair generations per second");
        }
        public void Test1_3()
        {
            int n = 10000;
            _wtl("ECDH deriving shared key...");
            using (var serverECDH = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256))
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

        static void Send(byte[] key, string secretMessage, out byte[] encryptedMessage, out byte[] iv)
        {
            using (Aes aes = new AesCryptoServiceProvider())
            {
                aes.Key = key;
                iv = aes.IV;

                // Encrypt the message
                using (MemoryStream ciphertext = new MemoryStream())
                using (CryptoStream cs = new CryptoStream(ciphertext, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    byte[] plaintextMessage = Encoding.UTF8.GetBytes(secretMessage);
                    cs.Write(plaintextMessage, 0, plaintextMessage.Length);
                    cs.Close();
                    encryptedMessage = ciphertext.ToArray();
                }
            }
        }
        
        static string Receive(byte[] key, byte[] encryptedMessage, byte[] iv)
        {

            using (Aes aes = new AesCryptoServiceProvider())
            {
                aes.Key = key;
                aes.IV = iv;
                // Decrypt the message
                using (MemoryStream plaintext = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(plaintext, aes.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(encryptedMessage, 0, encryptedMessage.Length);
                        cs.Close();
                        return Encoding.UTF8.GetString(plaintext.ToArray());
                    }
                }
            }
        }
    }
   
}
