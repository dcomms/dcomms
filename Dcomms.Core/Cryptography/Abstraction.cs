﻿using Org.BouncyCastle.Math.EC.Rfc8032;
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
        
        void GenerateEcdh25519Keypair(out byte[] localEcdhPrivateKey, out byte[] localEcdhPublicKey);
        byte[] DeriveEcdh25519SharedSecret(byte[] localPrivateKey, byte[] remotePublicKey);
    }

    public static class CryptoLibraries
    {
        public static ICryptoLibrary Library => new CryptoLibrary1();

        public static readonly int Ed25519PublicKeySize = Ed25519.PublicKeySize;
        public static readonly int Ed25519SignatureSize = Ed25519.SignatureSize;

        public static readonly int Ecdh25519PrivateKeySize = Org.BouncyCastle.Crypto.Parameters.X25519PrivateKeyParameters.KeySize;
        public static readonly int Ecdh25519PublicKeySize = Org.BouncyCastle.Crypto.Parameters.X25519PublicKeyParameters.KeySize;
        public static readonly int Ecdh25519SharedSecretKeySize = Org.BouncyCastle.Crypto.Parameters.X25519PrivateKeyParameters.SecretSize;
    }
}
