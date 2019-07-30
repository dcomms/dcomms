using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math.EC.Rfc8032;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Dcomms.Cryptography
{
    class CryptoLibrary1 : ICryptoLibrary
    {       
        readonly SHA256 _sha256 = SHA256.Create();
        byte[] ICryptoLibrary.GetHashSHA256(byte[] data)
        {
            return _sha256.ComputeHash(data);
        }

        readonly SHA512 _sha512 = SHA512.Create();
        byte[] ICryptoLibrary.GetHashSHA512(byte[] data)
        {
            return _sha512.ComputeHash(data);
        }
        byte[] ICryptoLibrary.GetHashSHA512(Stream data)
        {
            return _sha512.ComputeHash(data);
        }

        byte[] ICryptoLibrary.GeneratePrivateKeyEd25519()
        {
            var data = new byte[Ed25519.SecretKeySize];
            Ed25519.GeneratePrivateKey(new Org.BouncyCastle.Security.SecureRandom(), data);
            return data;
        }

        byte[] ICryptoLibrary.SignEd25519(byte[] plainText, byte[] privateKey)
        {
            var signer = new Ed25519Signer();
            signer.Init(true, new Ed25519PrivateKeyParameters(privateKey, 0));
            signer.BlockUpdate(plainText, 0, plainText.Length);
            return signer.GenerateSignature();
        }

        bool ICryptoLibrary.VerifyEd25519(byte[] plainText, byte[] signature, byte[] publicKey)
        {
            var signer = new Ed25519Signer();
            signer.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
            signer.BlockUpdate(plainText, 0, plainText.Length);
            return signer.VerifySignature(signature);
        }
        byte[] ICryptoLibrary.GetPublicKeyEd25519(byte[] privateKey)
        {
            byte[] publicKey = new byte[Ed25519.PublicKeySize];
            Ed25519.GeneratePublicKey(privateKey, 0, publicKey, 0);
            return publicKey;
        }
    }
}
