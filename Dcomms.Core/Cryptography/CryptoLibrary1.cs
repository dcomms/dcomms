using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Dcomms.Cryptography
{
    class CryptoLibrary1 : ICryptoLibrary
    {
        ISharedSecret ICryptoLibrary.DiffieHellmanKeyExchangeProcedure(IPublicKey publicKey, IPrivateKey privateKey)
        {
            throw new NotImplementedException();
        }

        void ICryptoLibrary.GenerateKeyPair(out IPublicKey publicKey, out IPrivateKey privateKey)
        {
            throw new NotImplementedException();
        }

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

        ISignature ICryptoLibrary.SignCertificate(IPrivateKey issuersPrivateKey, ICertificate certificate)
        {
            throw new NotImplementedException();
        }
    }
}
