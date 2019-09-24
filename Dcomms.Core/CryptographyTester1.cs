using Dcomms.CCP;
using Dcomms.Cryptography;
using Dcomms.DRP;
using Dcomms.Sandbox;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Dcomms
{
    public class CryptographyTester1 :IDisposable
    {
        readonly VisionChannel _visionChannel;
        public CryptographyTester1(VisionChannel visionChannel)
        {
            _visionChannel = visionChannel;
        }

        ICryptoLibrary _cryptoLibrary = CryptoLibraries.Library;
        public int TestSha256inputSize { get; set; } = 128;
        public DelegateCommand TestSha256 => new DelegateCommand(() =>
        {
            var sw = Stopwatch.StartNew();
            var input = new byte[TestSha256inputSize];
            var rnd = new Random();
            rnd.NextBytes(input);
            int n = 100000;
            for (int i = 0; i < n; i++)
            {
                _cryptoLibrary.GetHashSHA256(input);
            }
            sw.Stop();
            var callsPerSec = (double)n / sw.Elapsed.TotalSeconds;
            _visionChannel.Emit(null, null, AttentionLevel.detail, $"calls per sec: {callsPerSec}");
        });
        
        public int TestSha512inputSize { get; set; } = 128;
        public DelegateCommand TestSha512 => new DelegateCommand(() =>
        {
            var sw = Stopwatch.StartNew();
            var input = new byte[TestSha256inputSize];
            var rnd = new Random();
            rnd.NextBytes(input);
            int n = 100000;
            for (int i = 0; i < n; i++)
            {
                _cryptoLibrary.GetHashSHA512(input);
            }
            sw.Stop();
            var callsPerSec = (double)n / sw.Elapsed.TotalSeconds;
            _visionChannel.Emit(null, null, AttentionLevel.detail, $"calls per sec: {callsPerSec}");
        });

        public DelegateCommand TestPoW_CCP_hello0 => new DelegateCommand(() =>
        {
            int n = 50;
            var sb = new StringBuilder();
            double totalMs = 0;
            double maxMs = 0;
            for (int i = 0; i < n; i++)
            {
                var sw = Stopwatch.StartNew();
                var cnonce0 = new byte[ClientHelloPacket0.Cnonce0SupportedSize];
                var rnd = new Random();
                rnd.NextBytes(cnonce0);
                var addressBytes = new byte[4]; rnd.NextBytes(addressBytes);
                CcpClient.GenerateNewClientHelloPacket0(addressBytes, MiscProcedures.DateTimeToUint32seconds(DateTime.UtcNow), cnonce0);
                sw.Stop();

                sb.AppendFormat("{0:0} ", sw.Elapsed.TotalMilliseconds);
                totalMs += sw.Elapsed.TotalMilliseconds;
                if (sw.Elapsed.TotalMilliseconds > maxMs) maxMs = sw.Elapsed.TotalMilliseconds;
            }

            _visionChannel.Emit(null, null, AttentionLevel.detail, $"delays: {sb}.\r\nmax: {maxMs}ms\r\naverage: {totalMs/n}ms"); 
            // asv huawei 

        });
        public DelegateCommand TestEd25519 => new DelegateCommand(() =>
        {
            var privateKey = _cryptoLibrary.GeneratePrivateKeyEd25519();
            var publicKey = _cryptoLibrary.GetPublicKeyEd25519(privateKey);

            var plainText = new byte[128];
            var rnd = new Random();
            rnd.NextBytes(plainText);

            byte[] signature = null;
            var swSign = Stopwatch.StartNew();
            int nSign = 10000;
            for (int i = 0; i < nSign; i++)
            {
                signature = _cryptoLibrary.SignEd25519(plainText, privateKey);               
            }
            swSign.Stop();
            var signaturesPerSecond = (double)nSign / swSign.Elapsed.TotalSeconds;


            var swVerify = Stopwatch.StartNew();
            int nVerify = 10000;
            for (int i = 0; i < nVerify; i++)
            {
                if (!_cryptoLibrary.VerifyEd25519(plainText, signature, publicKey))
                    throw new Exception();
            }
            swVerify.Stop();
            var verificationsPerSecond = (double)nVerify / swVerify.Elapsed.TotalSeconds;

            _visionChannel.Emit(null, null, AttentionLevel.detail, $"Ed25519: { signaturesPerSecond } sign/sec, { verificationsPerSecond } ver/sec");
            // asv huawei 

        });
        public DelegateCommand TestEcdh25519 => new DelegateCommand(() =>
        {

            var sw = Stopwatch.StartNew();
            int n = 10000;
            for (int i = 0; i < n; i++)
            {
                _cryptoLibrary.GenerateEcdh25519Keypair(out var privateKeyA, out var publicKeyA);
                _cryptoLibrary.GenerateEcdh25519Keypair(out var privateKeyB, out var publicKeyB);

                var sharedKeyAB = _cryptoLibrary.DeriveEcdh25519SharedSecret(privateKeyA, publicKeyB);
                var sharedKeyBA = _cryptoLibrary.DeriveEcdh25519SharedSecret(privateKeyB, publicKeyA);
             
            }
            sw.Stop();

            _visionChannel.Emit(null, null, AttentionLevel.detail, $"Ecdh25519: { (double)n / sw.Elapsed.TotalSeconds } fullABop/sec");
         
        });
        
        public DelegateCommand TestAes => new DelegateCommand(() =>
        {
            _cryptoLibrary.GenerateEcdh25519Keypair(out var privateKeyA, out var publicKeyA);
            _cryptoLibrary.GenerateEcdh25519Keypair(out var privateKeyB, out var publicKeyB);

            var sharedKeyAB = _cryptoLibrary.DeriveEcdh25519SharedSecret(privateKeyA, publicKeyB);
            var sharedKeyBA = _cryptoLibrary.DeriveEcdh25519SharedSecret(privateKeyB, publicKeyA);

            var input = new byte[4+2+2    + 8];  // ip addr 4 bytes; port 2 bytes; token16 2 bytes
            for (byte i = 0; i < input.Length; i++)
                input[i] = i;
            var output = new byte[input.Length];
            var decrypted = new byte[input.Length];

            var iv = _cryptoLibrary.GetHashSHA256(sharedKeyAB).Take(16).ToArray();
            var sw = Stopwatch.StartNew();
            int n = 300000;
            for (int i = 0; i < n; i++)
            {
                _cryptoLibrary.ProcessAesCbcBlocks(true, sharedKeyAB, iv, input, output);
                _cryptoLibrary.ProcessAesCbcBlocks(false, sharedKeyAB, iv, output, decrypted);
            }
            sw.Stop();

            _visionChannel.Emit(null, null, AttentionLevel.detail, $"AES: { (double)n / sw.Elapsed.TotalSeconds } fullOp/sec");           

        });
        
        DrpTester1 _drpTester1;
        public DelegateCommand CreateDrpTester1 => new DelegateCommand(() =>
        {
            if (_drpTester1 != null) throw new InvalidOperationException();
            _drpTester1 = new DrpTester1(_visionChannel);
        });
        public DelegateCommand CreateDrpTester1_SendInvite => new DelegateCommand(() =>
        {
            if (_drpTester1 != null) throw new InvalidOperationException();
            _drpTester1 = new DrpTester1(_visionChannel, ()=>
            {
                _ = _drpTester1.SendInvite_AtoX_Async();
            });
        });


        DrpTester2 _drpTester2;
        public DelegateCommand CreateDrpTester2 => new DelegateCommand(() =>
        {
            if (_drpTester2 != null) throw new InvalidOperationException();
            _drpTester2 = new DrpTester2(_visionChannel);
        });

        public DelegateCommand TestUniqueDataTracker => new DelegateCommand(() =>
        {
           // var t = new UniqueDataTracker();
         //   t.TryInputData(null);
        });

        public void Dispose()
        {
            if (_drpTester1 != null) _drpTester1.Dispose();
            if (_drpTester2 != null) _drpTester2.Dispose();
        }
    }
}
