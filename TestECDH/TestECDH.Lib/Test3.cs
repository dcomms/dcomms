using Elliptic;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace TestECDH.Lib
{
    public class Test3
    {

        readonly Action<string> _wtl;
        public Test3(Action<string> wtl)
        {
            _wtl = wtl;
        }

        public void GenerateCurve25519publicKeys()
        {
            int n = 0;
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < 50; i++)
            {
                byte[] privateKey = Curve25519.ClampPrivateKey(GetUniformBytes((byte)i, 32));
                for (int j = 0; j < 200; j++)
                {
                    byte[] publicKey = Curve25519.GetPublicKey(privateKey);
                    n++;
                }
            }
            sw.Stop();

            var callsPerSec = (double)n / sw.Elapsed.TotalSeconds;
            _wtl($"Curve25519 public keys generated per sec: {callsPerSec}");
        }

      //  todo shared key derivation

        public static Random CreateSemiRandomGenerator()
        {
            DateTime now = DateTime.Now;
            return new Random(now.DayOfYear * 365 + now.Hour);
        }

        public static byte[] GetRandomBytes(Random random, int size)
        {
            byte[] result = new byte[size];
            for (int i = 0; i < size; i++)
            {
                result[i] = (byte)random.Next(256);
            }
            return result;
        }

        public static byte[] GetUniformBytes(byte value, int size)
        {
            byte[] result = new byte[size];
            for (int i = 0; i < size; i++)
            {
                result[i] = value;
            }
            return result;
        }

        public static byte[] ToggleBitInKey(byte[] buffer, Random random)
        {
            var bitArray = new BitArray(buffer);
            var bitToToggle = random.Next(buffer.Length * 8);
            var bit = bitArray.Get(bitToToggle);
            bitArray.Set(bitToToggle, !bit);

            var result = new byte[buffer.Length];
            bitArray.CopyTo(result, 0);
            return result;
        }
    }
}
