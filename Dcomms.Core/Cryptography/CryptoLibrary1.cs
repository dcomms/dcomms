﻿using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math.EC.Rfc8032;
using Org.BouncyCastle.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Dcomms.Cryptography
{
    class CryptoLibrary1 : ICryptoLibrary
    {
        SecureRandom _secureRandom = new SecureRandom();
        SecureRandom _secureRandom2 = new SecureRandom();
        SecureRandom _secureRandom3 = new SecureRandom();

        byte[] ICryptoLibrary.GetRandomBytes(int count)
        {
            var r = new byte[count];
            _secureRandom.NextBytes(r);
            return r;
        }


        readonly SHA256 _sha256 = SHA256.Create();
        public byte[] GetHashSHA256(byte[] data)
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
            Ed25519.GeneratePrivateKey(_secureRandom2, data);
            return data;
        }

        byte[] ICryptoLibrary.SignEd25519(byte[] plainText, byte[] privateKey)
        {
            if (privateKey == null) throw new ArgumentException(nameof(privateKey));
            if (plainText == null) throw new ArgumentException(nameof(plainText));
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
        void ICryptoLibrary.GenerateEcdh25519Keypair(out byte[] localEcdhPrivateKey, out byte[] localEcdhPublicKey)
        {
            X25519PrivateKeyParameters privateKey = new X25519PrivateKeyParameters(_secureRandom3);
            localEcdhPrivateKey = privateKey.GetEncoded();
            localEcdhPublicKey = privateKey.GeneratePublicKey().GetEncoded();
        }
        byte[] ICryptoLibrary.DeriveEcdh25519SharedSecret(byte[] localPrivateKey, byte[] remotePublicKey)
        {
            var sharedSecret = new byte[CryptoLibraries.Ecdh25519SharedSecretKeySize];
            new X25519PrivateKeyParameters(localPrivateKey, 0)
                .GenerateSecret(
                    new X25519PublicKeyParameters(remotePublicKey, 0),
                    sharedSecret,
                    0
                );
            return sharedSecret;
        }
        void ICryptoLibrary.ProcessAesCbcBlocks(bool encryptOrDecrypt, byte[] key, byte[] iv, byte[] input, byte[] output)
        {
            if (input.Length != output.Length) throw new ArgumentException();

            var cbcBlockCipher = new CbcBlockCipher(new AesEngine());
            var blockSize = CryptoLibraries.AesBlockSize;
            if (input.Length % blockSize != 0) throw new ArgumentException();
            cbcBlockCipher.Init(encryptOrDecrypt, new ParametersWithIV(new KeyParameter(key), iv));
            var numberOfBlocks = input.Length / blockSize;
            int offset = 0;
            for (int i = 0; i < numberOfBlocks; i++)
            {
                cbcBlockCipher.ProcessBlock(input, offset, output, offset);
                offset += blockSize;
            }
        }
        //void ICryptoLibrary.ProcessAesGcmBlocks(bool encryptOrDecrypt, byte[] key, byte[] iv, byte[] input, byte[] output)
        //{
        //    if (input.Length != output.Length) throw new ArgumentException();

        //    var gcmBlockCipher = new GcmBlockCipher(new AesEngine());
        //    var blockSize = CryptoLibraries.AesBlockSize;
        //    if (input.Length % blockSize != 0) throw new ArgumentException();
        //    gcmBlockCipher.Init(encryptOrDecrypt, new ParametersWithIV(new KeyParameter(key), iv));
        //    var numberOfBlocks = input.Length / blockSize;
        //    int offset = 0;
        //    for (int i = 0; i < numberOfBlocks; i++)
        //    {
        //        gcmBlockCipher.ProcessAadBytes();
        //        gcmBlockCipher.ProcessBytes();
        //        gcmBlockCipher.GetMac();
                    
                    
        //          //  ProcessBlock(input, offset, output, offset);
        //        offset += blockSize;
        //    }
        //}

        byte[] ICryptoLibrary.GetSha256HMAC(byte[] key, byte[] data)
        {
            var hmac = new HMac(new Sha256Digest());
            hmac.Init(new KeyParameter(key));
            var result = new byte[hmac.GetMacSize()];

            hmac.BlockUpdate(data, 0, data.Length);

            hmac.DoFinal(result, 0);
            return result;
        }



        void ICryptoLibrary.DeriveKeysRFC5869_32bytes(byte[] input, byte[] salt, out byte[] key1, out byte[] key2)
        {
            var hkdf = new HkdfBytesGenerator(new Sha256Digest());
            hkdf.Init(new HkdfParameters(input, salt, null));

            key1 = new byte[32];
            hkdf.GenerateBytes(key1, 0, key1.Length);
            key2 = new byte[32];
            hkdf.GenerateBytes(key2, 0, key2.Length);
        }
    }
}
