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
        /// <summary>
        /// includes "root keys digital signature algorithm type": "ed25519", "rsa2048"
        /// </summary>
        byte Flags;
        const byte FlagsMask_MustBeZero = 0b11110000;
        public TimeSpan MaxCertificateDuration; // zero if no intermediate certificates are allowed: only live certificate generation with direct access to root key
        public DateTime UserIdMaintenanceReplacementMaxTimeUTC; // similar to credit card "expiration date"
        public DateTime UserIdMaintenanceReplacementMinTimeUTC;
        public List<byte[]> RootPublicKeys;
        public int GetSignatureSize(int keyIndex)
        {
            if (keyIndex < 0 || keyIndex >= RootPublicKeys.Count)
                throw new ArgumentException();
            return CryptoLibraries.Ed25519SignatureSize;
        }
        public byte MinimalRequiredRootSignaturesCountInCertificate;
        
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
            return String.Join(";", RootPublicKeys.Select(x => MiscProcedures.ByteArrayToString(x)));
        }
               
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Flags);
            // if (CertificateEd25519Signature.Length != CryptoLibraries.Ed25519SignatureSize) throw new ArgumentException();
            //  writer.Write(CertificateEd25519Signature);
            throw new NotImplementedException();

        }
        static UserID_PublicKeys Decode(BinaryReader reader)
        {
            var r = new UserID_PublicKeys();
            r.Flags = reader.ReadByte();
            if ((r.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            // r.CertificateEd25519Signature = reader.ReadBytes(CryptoLibraries.Ed25519SignatureSize);
            throw new NotImplementedException();
            return r;
        }
        public void AssertIsValidNow(DateTime localTimeNowUtc)
        {
            if (localTimeNowUtc > UserIdMaintenanceReplacementMinTimeUTC) throw new ExpiredUserKeysException();
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

        public static void CreateUserKeyPairs(int numberOfKeyPairs, ICryptoLibrary cryptoLibrary, out UserRootPrivateKeys privateKeys, out UserID_PublicKeys publicKeys)
        {
            if (numberOfKeyPairs <= 0 || numberOfKeyPairs > 3) throw new ArgumentException(); // the UDP packets can go over 1000 bytes to transfer a big UsersCertificate
            privateKeys = new UserRootPrivateKeys();
            privateKeys.ed25519privateKeys = new List<byte[]>();
            publicKeys = new UserID_PublicKeys();
            publicKeys.MinimalRequiredRootSignaturesCountInCertificate = x;
            publicKeys.MaxCertificateDuration = x;
            publicKeys.UserIdMaintenanceRefreshMinTimeUTC = x;
            publicKeys.RootPublicKeys = new List<byte[]>();

            for (int i = 0; i < numberOfKeyPairs; i++)
            {
                var privateKey = cryptoLibrary.GeneratePrivateKeyEd25519();
                var publicKey = cryptoLibrary.GetPublicKeyEd25519(privateKey);
                privateKeys.ed25519privateKeys.Add(privateKey);
                publicKeys.RootPublicKeys.Add(publicKey);
            }
        }
    }
    
    public class UserRootSignature
    {
        public byte RootKeyIndex;

        public byte[] SignatureSalt;
        public const int SignatureSaltSize = 4;

        /// <summary>
        /// signatures by UserRootPrivateKeys[RootKeyIndex].   size of signature is defined by type of root key
        /// 
        /// sign {
        ///   UserCertificate._validFromUtc32minutes,
        ///   UserCertificate._validToUtc32minutes,
        ///   UserCertificate.CertificateEd25519publicKey
        ///   this.SignatureSalt
        /// }
        /// </summary>
        public byte[] Signature;

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
        byte Flags; // will include CertificatePublicKey algorithm type = "ed25519" by default
        const byte FlagsMask_MustBeZero = 0b11110000;

        uint _validFromUtc32minutes;
        public DateTime ValidFromUtc => MiscProcedures.Uint32minutesToDateTime(_validFromUtc32minutes);
        uint _validToUtc32minutes;
        public DateTime ValidToUtc => MiscProcedures.Uint32minutesToDateTime(_validToUtc32minutes);
        /// <summary>
        /// can be equal to UserID_PublicKeys.Ed25519UserRootPublicKeys[x], in case of HSM module, that is available in live mode to authenticate user's messages
        /// in this case UserRootSignatures can be empty, or contain less items
        /// </summary>
        public byte[] CertificatePublicKey;
        /// <summary>
        /// is null for certificates received from remote side
        /// </summary>
        public byte[] LocalCertificatePrivateKey; 

        public List<UserRootSignature> UserRootSignatures;

        /// <summary>
        /// the procedure verifies certificate that is received from remote party to local party 
        /// throws exception if the certificate is invalid for the specified userId   (the caller knows that certificate came from the userId)
        /// also checks userId - expired or not
        /// </summary>
        void AssertIsValidNow(ICryptoLibrary cryptoLibrary, UserID_PublicKeys userId, DateTime localTimeNowUtc)
        {
            userId.AssertIsValidNow(localTimeNowUtc);

            if (MiscProcedures.EqualByteArrays(this.CertificatePublicKey, userId.RootPublicKeys[i]))
            {
                // mode without intermediate certificate private key
                
                // todo  userId.MinimalRequiredRootSignaturesCountInCertificate

            }


            if (userId.Ed25519UserRootPublicKeys.Count != this.Ed25519UserRootSignatures.Count)
                throw new BadUserCertificateException();
            if (userId.Ed25519UserRootPublicKeys.Count != this.UserRootSignaturesSalts.Count)
                throw new BadUserCertificateException();

            if (localTimeNowUtc > ValidToUtc) throw new CertificateOutOfDateException();
            if (localTimeNowUtc < ValidFromUtc) throw new CertificateOutOfDateException();


            if (ValidToUtc -ValidFromUtc > userId.MaxCertificateDuration)
                throw new BadUserCertificateException();

           // todo  userId.MinimalRequiredRootSignaturesCountInCertificate


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

        public void Encode(BinaryWriter writer)
        {
            writer.Write(ReservedFlagsMustBeZero);
            writer.Write(_validFromUtc32minutes);
            writer.Write(_validToUtc32minutes);
            if (CertificateEd25519publicKey.Length != CryptoLibraries.Ed25519PublicKeySize) throw new ArgumentException();
            writer.Write(CertificateEd25519publicKey);

            if (UserRootSignaturesSalts.Count == 0 || UserRootSignaturesSalts.Count > 255) throw new ArgumentException();
            if (UserRootSignaturesSalts.Count != Ed25519UserRootSignatures.Count) throw new ArgumentException();

            writer.Write((byte)UserRootSignaturesSalts.Count);
            for (int i = 0; i < UserRootSignaturesSalts.Count; i++)
            {
                var salt = UserRootSignaturesSalts[i];
                if (salt.Length != UserRootSignaturesSaltSize) throw new ArgumentException();
                writer.Write(salt);
                var signature = Ed25519UserRootSignatures[i];
                if (signature.Length != CryptoLibraries.Ed25519SignatureSize) throw new ArgumentException();
                writer.Write(signature);
            }
        }
        /// <summary>
        /// throws exception if the certificate is invalid for the specified userId   (the caller knows that certificate came from the userId)
        /// </summary>
        public static UserCertificate Decode_AssertIsValidNow(BinaryReader reader, ICryptoLibrary cryptoLibrary, UserID_PublicKeys userId, DateTime localTimeNowUtc)
        {
            var r = new UserCertificate();
            r.Flags = reader.ReadByte();
            if ((r.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            r._validFromUtc32minutes = reader.ReadUInt32();
            r._validToUtc32minutes = reader.ReadUInt32();
            r.CertificatePublicKey = reader.ReadBytes(CryptoLibraries.Ed25519PublicKeySize);
            var userRootSignaturesCount = reader.ReadByte();
            r.UserRootSignatures = new List<UserRootSignature>(userRootSignaturesCount);
            for (int i = 0; i < userRootSignaturesCount; i++)
            {
                var userRootSignature = new UserRootSignature();
                userRootSignature.RootKeyIndex = reader.ReadByte();
                userRootSignature.SignatureSalt = reader.ReadBytes(UserRootSignature.SignatureSaltSize);
                userRootSignature.Signature = reader.ReadBytes(userId.GetSignatureSize(userRootSignature.RootKeyIndex));
                r.UserRootSignatures.Add(userRootSignature);
            }

            r.AssertIsValidNow(cryptoLibrary, userId, localTimeNowUtc);
            return r;
        }
    }
    public class UserCertificateSignature
    {
        byte Flags; 
        const byte FlagsMask_MustBeZero = 0b11110000;

        public byte[] CertificateSignature;
        public static UserCertificateSignature Sign(ICryptoLibrary cryptoLibrary, Action<BinaryWriter> writeSignedFields, UserCertificate userCertificateWithPrivateKey)
        {
            var r = new UserCertificateSignature();
            var ms = new MemoryStream(); using (var writer = new BinaryWriter(ms)) writeSignedFields(writer);
            r.CertificateSignature = cryptoLibrary.SignEd25519(
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
            if (cryptoLibrary.VerifyEd25519(signedData.ToArray(), CertificateSignature, userCertificateWithPublicKey.CertificatePublicKey) == false)
                return false;
            return true;
        }

        public void Encode(BinaryWriter writer)
        {
            writer.Write(Flags);
            if (CertificateSignature.Length != CryptoLibraries.Ed25519SignatureSize) throw new ArgumentException();
            writer.Write(CertificateSignature);

        }
        static UserCertificateSignature Decode(BinaryReader reader)
        {
            var r = new UserCertificateSignature();
            r.Flags = reader.ReadByte();
            if ((r.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            r.CertificateSignature = reader.ReadBytes(CryptoLibraries.Ed25519SignatureSize);
            return r;
        }
    }
}
