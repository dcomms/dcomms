using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Dcomms.DMP
{
    public class UserID_PublicKeys
    {
        //  byte ReservedFlagsMustBeZero; // will include "type" = "ed25519 by default"
        public List<byte[]> Ed25519UserRootPublicKeys;

        public UserID_PublicKeys(List<byte[]> ed25519UserRootPublicKeys)
        {
            Ed25519UserRootPublicKeys = ed25519UserRootPublicKeys;
        }

        public override bool Equals(object obj)
        {
            var obj2 = (UserID_PublicKeys)obj;
            if (obj2.Ed25519UserRootPublicKeys.Count != this.Ed25519UserRootPublicKeys.Count)
                return false;

            for (int i = 0; i < this.Ed25519UserRootPublicKeys.Count; i++)
                if (!MiscProcedures.EqualByteArrays(obj2.Ed25519UserRootPublicKeys[i], this.Ed25519UserRootPublicKeys[i]))
                    return false;

            return true;
        }
        public override int GetHashCode()
        {
            int r = 0;
            foreach (var a in Ed25519UserRootPublicKeys)
                r ^= MiscProcedures.GetArrayHashCode(a);
            return r;
        }
        public override string ToString()
        {
            return String.Join(";", Ed25519UserRootPublicKeys.Select(x => MiscProcedures.ByteArrayToString(x)));
        }
    }

    public class UserRootPrivateKeys
    {
        /// <summary>
        /// are used to sign (intermediate) UserCertificate's
        /// signature of every private key is required
        /// 
        /// the private keys will be separated later, to be stored on separate independent devices
        /// </summary>
        public List<byte[]> ed25519privateKeys;
    }

    /// <summary>
    /// intermediate certificate, signed by root user's keys
    /// is stored at messenger device,
    /// is used to sign SessionDescription's
    /// 
    /// does not contain UserID, as it comes from local/verifier user's contact book: verifier knows which root keys must issue the certificate, by RegID
    /// </summary>
    public class UserCertificate
    {
        uint _validFromUtc32minutes;
        public DateTime ValidFromUtc => MiscProcedures.Uint32minutesToDateTime(_validFromUtc32minutes);
        uint _validToUtc32minutes;
        public DateTime ValidToUtc => MiscProcedures.Uint32minutesToDateTime(_validToUtc32minutes);
        public byte[] CertificateEd25519publicKey;
        public byte[] LocalCertificateEd25519PrivateKey; // is null for certificates received from remote side
        public List<byte[]> UserRootSignaturesSalts;
        const int UserRootSignaturesSaltSize = 4;

        /// <summary>
        /// multiple (each one is required) signatures by UserRootPrivateKeys
        /// 
        /// sign {
        ///   _validFromUtc32minutes,
        ///   _validToUtc32minutes,
        ///   CertificateEd25519publicKey
        ///   Ed25519UserRootSignaturesSalts[i]
        /// }
        /// </summary>
        public List<byte[]> Ed25519UserRootSignatures;

        /// <summary>
        /// throws exception if the certificate is invalid for the specified userId   (the caller knows that certificate came from the userId)
        /// </summary>
        public void AssertIsValidNow(ICryptoLibrary cryptoLibrary, UserID_PublicKeys userId, DateTime localTimeNowUtc)
        {
            if (userId.Ed25519UserRootPublicKeys.Count != this.Ed25519UserRootSignatures.Count)
                throw new BadUserCertificateException();
            if (userId.Ed25519UserRootPublicKeys.Count != this.UserRootSignaturesSalts.Count)
                throw new BadUserCertificateException();

            if (localTimeNowUtc > ValidToUtc) throw new CertificateOutOfDateException();
            if (localTimeNowUtc < ValidFromUtc) throw new CertificateOutOfDateException();

            for (int i = 0; i < userId.Ed25519UserRootPublicKeys.Count; i++)
            {
                var ed25519UserRootPublicKey = userId.Ed25519UserRootPublicKeys[i];
                var ed25519UserRootSignature = this.Ed25519UserRootSignatures[i];

                var signedData = new MemoryStream();
                using (var writer = new BinaryWriter(signedData))
                    WriteSignedFields(writer, i);

                if (cryptoLibrary.VerifyEd25519(signedData.ToArray(), ed25519UserRootSignature, ed25519UserRootPublicKey) == false)
                    throw new BadUserCertificateException();
            }
        }
        void WriteSignedFields(BinaryWriter writer, int index)
        {
            writer.Write(_validFromUtc32minutes);
            writer.Write(_validToUtc32minutes);
            writer.Write(CertificateEd25519publicKey);
            writer.Write(UserRootSignaturesSalts[index]);
        }
        public static UserCertificate GenerateKeyPairsAndSignAtSingleDevice(ICryptoLibrary cryptoLibrary, UserID_PublicKeys userId, UserRootPrivateKeys userRootPrivatekeys, DateTime fromUtc, DateTime toUtc)
        {
            if (userRootPrivatekeys.ed25519privateKeys.Count != userId.Ed25519UserRootPublicKeys.Count)
                throw new ArgumentException();
            var r = new UserCertificate();
            r._validFromUtc32minutes = MiscProcedures.DateTimeToUint32minutes(fromUtc);
            r._validToUtc32minutes = MiscProcedures.DateTimeToUint32minutes(toUtc);            
            r.LocalCertificateEd25519PrivateKey = cryptoLibrary.GeneratePrivateKeyEd25519();
            r.CertificateEd25519publicKey = cryptoLibrary.GetPublicKeyEd25519(r.LocalCertificateEd25519PrivateKey);
            r.UserRootSignaturesSalts = new List<byte[]>();
            r.Ed25519UserRootSignatures = new List<byte[]>();
            for (int i = 0; i < userId.Ed25519UserRootPublicKeys.Count; i++)
            {
                var salt = cryptoLibrary.GetRandomBytes(UserRootSignaturesSaltSize);
                r.UserRootSignaturesSalts.Add(salt);                
                var ms = new MemoryStream(); using (var writer = new BinaryWriter(ms)) r.WriteSignedFields(writer, i);
                r.Ed25519UserRootSignatures.Add(cryptoLibrary.SignEd25519(ms.ToArray(), userRootPrivatekeys.ed25519privateKeys[i]));
            }
            return r;
        }

        public void Encode()
        {
            throw new NotImplementedException();
        }
        public static UserCertificate Decode()
        {
            throw new NotImplementedException();
        }
    }
    public class UserCertificateSignature
    {
        byte ReservedFlagsMustBeZero; // will include "type" = "ed25519 by default"

        public byte[] CertificateEd25519Signature;
        public static UserCertificateSignature Sign(ICryptoLibrary cryptoLibrary, Action<BinaryWriter> writeSignedFields, UserCertificate userCertificateWithPrivateKey)
        {
            var r = new UserCertificateSignature();
            var ms = new MemoryStream(); using (var writer = new BinaryWriter(ms)) writeSignedFields(writer);
            r.CertificateEd25519Signature = cryptoLibrary.SignEd25519(
                    ms.ToArray(),
                    userCertificateWithPrivateKey.LocalCertificateEd25519PrivateKey);
            return r;
        }
       
        public static UserCertificateSignature DecodeAndVerify(BinaryReader reader, ICryptoLibrary cryptoLibrary, Action<BinaryWriter> writeSignedFields, UserCertificate userCertificateWithPublicKey)
        {
            var r = Decode(reader);
            if (!r.Verify(cryptoLibrary, writeSignedFields, userCertificateWithPublicKey)) throw new BadSignatureException();
            return r;
        }
        public bool Verify(ICryptoLibrary cryptoLibrary, Action<BinaryWriter> writeSignedFields, UserCertificate userCertificateWithPublicKey)
        {
            var signedData = new MemoryStream();
            using (var writer = new BinaryWriter(signedData))
                writeSignedFields(writer);
            if (cryptoLibrary.VerifyEd25519(signedData.ToArray(), CertificateEd25519Signature, userCertificateWithPublicKey.CertificateEd25519publicKey) == false)
                return false;
            return true;
        }

        public void Encode(BinaryWriter writer)
        {
            writer.Write(ReservedFlagsMustBeZero);
            if (CertificateEd25519Signature.Length != CryptoLibraries.Ed25519SignatureSize) throw new ArgumentException();
            writer.Write(CertificateEd25519Signature);

        }
        static UserCertificateSignature Decode(BinaryReader reader)
        {
            var r = new UserCertificateSignature();
            r.ReservedFlagsMustBeZero = reader.ReadByte();
            r.CertificateEd25519Signature = reader.ReadBytes(CryptoLibraries.Ed25519SignatureSize);
            return r;
        }
    }
}
