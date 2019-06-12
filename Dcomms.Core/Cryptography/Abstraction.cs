using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.Cryptography
{
    public struct CommonName
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
    public interface ICertificate
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
    public interface IPublicKey
    {
        byte[] Data { get; }
        string HashForCommonName { get; }
        byte[] Encrypt(byte[] data);
        bool Verify(ISignature signature);
    }
    public interface IPrivateKey
    {
        byte[] Decrypt(byte[] data);
        ISignature Sign(byte[] data);
    }

    public interface ISignature
    {
        byte[] AsBytes(); 
        bool IsValid { get; }
    }

    public interface ISharedSecret
    {
        byte[] AbSharedSecret { get; }
    }


    public interface ICryptoLibrary
    {
        ISharedSecret DiffieHellmanKeyExchangeProcedure(IPublicKey publicKey, IPrivateKey privateKey);
        void GenerateKeyPair(out IPublicKey publicKey, out IPrivateKey privateKey);
        ISignature SignCertificate(IPrivateKey issuersPrivateKey, ICertificate certificate);

        byte[] GetHashSHA256(byte[] data);
        byte[] GetHashSHA512(byte[] data);
    }
    public static class CryptoLibraries
    {
        public static ICryptoLibrary Library => new CryptoLibrary1();
    }
}
