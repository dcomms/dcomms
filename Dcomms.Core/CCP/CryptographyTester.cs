﻿using Dcomms.CCP;
using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Dcomms.CCP
{
    public class CryptographyTester
    {
        readonly Action<string> _wtl;
        public CryptographyTester(Action<string> wtl)
        {
            _wtl = wtl;
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
            _wtl($"calls per sec: {callsPerSec}");
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
            _wtl($"calls per sec: {callsPerSec}");
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
                CcpClient.GenerateNewClientHelloPacket0(addressBytes, MiscProcedures.DateTimeToUint32(DateTime.UtcNow), cnonce0);
                sw.Stop();

                sb.AppendFormat("{0:0} ", sw.Elapsed.TotalMilliseconds);
                totalMs += sw.Elapsed.TotalMilliseconds;
                if (sw.Elapsed.TotalMilliseconds > maxMs) maxMs = sw.Elapsed.TotalMilliseconds;
            }

            _wtl($"delays: {sb}.\r\nmax: {maxMs}ms\r\naverage: {totalMs/n}ms"); 
            // asv huawei 

        });
        public DelegateCommand TestUniqueDataTracker => new DelegateCommand(() =>
        {
           // var t = new UniqueDataTracker();
         //   t.TryInputData(null);
        });
    }
}