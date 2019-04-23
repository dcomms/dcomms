using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.Cryptography
{
    struct CommonName
    {
        /// <summary>
        /// optional
        /// </summary>
        string Email { get; set; }
        /// <summary>
        /// optional
        /// </summary>
        string PhoneNumber { get; set; }
        /// <summary>
        /// required
        /// </summary>
        string HashedPublicKey { get; set; }
    }
    interface ICertificate
    {
        CommonName OwnerName { get; }
        DateTime From { get; }
        DateTime To { get; }
        CommonName IssuerName { get; }
        string PreviousCertificatesCompromisedDetails { get; set; }
        IPublicKey PublicKey { get; }
        /// <summary>
        /// comes from issuer of the certificate
        /// </summary>
        ISignature Signature { get;  }
    }
    interface IPublicKey
    {
        byte[] Data { get; }
        string HashForCommonName { get; }
        byte[] Encrypt(byte[] data);
        bool Verify(ISignature signature);
    }
    interface IPrivateKey
    {
        byte[] Decrypt(byte[] data);
        ISignature Sign(byte[] data);
    }

    interface ISignature
    {
        byte[] AsBytes(); 
        bool IsValid { get; }
    }

    interface ISharedSecret
    {
        byte[] AbSharedSecret { get; }
    }


    interface ICryptoLibrary
    {
        ISharedSecret DiffieHellmanKeyExchangeProcedure(IPublicKey publicKey, IPrivateKey privateKey);
        void GenerateKeyPair(out IPublicKey publicKey, out IPrivateKey privateKey);
        ISignature SignCertificate(IPrivateKey issuersPrivateKey, ICertificate certificate);
        /// <summary>
        /// SHA3 
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        byte[] GetHash(byte[] data);
    }
}
