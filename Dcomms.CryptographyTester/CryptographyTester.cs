using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Dcomms.CryptographyTester
{
    class CryptographyTester
    {
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
            MessageBox.Show($"calls per sec: {callsPerSec}");
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
            MessageBox.Show($"calls per sec: {callsPerSec}");
        });

        public DelegateCommand TestPoW_sha1906 => new DelegateCommand(() =>
        {
            var sw = Stopwatch.StartNew();
            var input = new byte[32];
            var rnd = new Random();

            const int indexStart = 4;
            const int indexEnd = 6;
            const byte value = 7;

            int i = 0;
            for (; ; i++)
            {
                rnd.NextBytes(input);
                var r = _cryptoLibrary.GetHashSHA512(input);
                bool ok = true;
                for (int j = indexStart; j < indexEnd; j++)
                    if (r[j] != value)
                    {
                        ok = false;
                        break;
                    }
                if (ok) break;
            }
            sw.Stop();
         
            MessageBox.Show($"{i} loops; {sw.Elapsed.TotalMilliseconds}ms");
        });
    }
}
