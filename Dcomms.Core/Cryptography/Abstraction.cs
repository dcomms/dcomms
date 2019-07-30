using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.Cryptography
{
    public interface ICryptoLibrary
    {
        byte[] GetHashSHA256(byte[] data);
        byte[] GetHashSHA512(byte[] data);
        byte[] GetHashSHA512(Stream data);


        byte[] SignEd25519(byte[] plainText, byte[] privateKey);
        bool VerifyEd25519(byte[] plainText, byte[] signature, byte[] publicKey);
        byte[] GeneratePrivateKeyEd25519();
        byte[] GetPublicKeyEd25519(byte[] privateKey);
    }

    public static class CryptoLibraries
    {
        public static ICryptoLibrary Library => new CryptoLibrary1();
    }
}
