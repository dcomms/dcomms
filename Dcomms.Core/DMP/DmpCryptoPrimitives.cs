﻿using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Dcomms.DMP
{
    /// <summary>
    /// = public root keys of the user
    /// </summary>
    public class UserId
    {
        /// <summary>
        /// includes "root keys digital signature algorithm type": "ed25519", "rsa2048"
        /// </summary>
        byte Flags;
        const byte FlagsMask_MustBeZero = 0b11100000;
        public const byte FlagsMask_UserIdReplacementIsAllowed = 0b00000001;

        /// <summary>
        /// specifies max duration of intermediate certificate, which is used for messaging while root private key is stored offline
        /// constrains creating of "too long" = insecure intermediate certificates
        /// zero if no intermediate certificates are allowed: only live certificate generation with direct access to root key
        /// </summary>
        public TimeSpan MaxCertificateDuration
        {
            get => TimeSpan.FromHours(_maxCertificateDurationHours);
            set
            {
                var h = value.TotalHours;
                if (h > ushort.MaxValue || h <= 0)
                    throw new ArgumentOutOfRangeException();
                _maxCertificateDurationHours = (ushort)h;
            }
        }
        ushort _maxCertificateDurationHours; 


        public byte MinimalRequiredRootSignaturesCountInCertificate;

        public List<byte[]> RootPublicKeys; // DSA type and public key size of all the items depends on the Flags. default is Ed25519
        public int GetSignatureSize(int keyIndex)
        {
            if (keyIndex < 0 || keyIndex >= RootPublicKeys.Count)
                throw new ArgumentException();
            return CryptoLibraries.Ed25519SignatureSize;
        }
        
        public override bool Equals(object obj)
        {
            var obj2 = (UserId)obj;
            if (obj2.RootPublicKeys.Count != this.RootPublicKeys.Count)
                return false;

            for (int i = 0; i < this.RootPublicKeys.Count; i++)
                if (!MiscProcedures.EqualByteArrays(obj2.RootPublicKeys[i], this.RootPublicKeys[i]))
                    return false;

            if (obj2._maxCertificateDurationHours != this._maxCertificateDurationHours) return false;
            return true;
        }
        public override int GetHashCode()
        {
            int r = 0;
            foreach (var a in RootPublicKeys)
                r ^= MiscProcedures.GetArrayHashCode(a);
            r ^= _maxCertificateDurationHours.GetHashCode();
            return r;
        }
        public override string ToString()
        {
            return String.Join(";", RootPublicKeys.Select(x => MiscProcedures.ByteArrayToString(x)));
        }

        public string ToCsharpDeclaration
        {
            get
            {
                var r = "RootPublicKeys = new List<byte[]> {\r\n";
                foreach (var x in RootPublicKeys)
                    r += $"new byte[] {{ {MiscProcedures.ByteArrayToCsharpDeclaration(x)}}},\r\n";
                r += "}";
                return r;
            }
        }
               
        public byte[] Encode()
        {
            BinaryProcedures.CreateBinaryWriter(out var ms, out var writer);
            Encode(writer);
            return ms.ToArray();
        }
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Flags);
            writer.Write(_maxCertificateDurationHours);
            writer.Write(MinimalRequiredRootSignaturesCountInCertificate);
            writer.Write((byte)RootPublicKeys.Count);
            foreach (var rootPublicKey in RootPublicKeys)
            {
                if (rootPublicKey.Length != CryptoLibraries.Ed25519PublicKeySize) throw new Exception("e984376");
                writer.Write(rootPublicKey);
            }
        }
        public static UserId Decode(byte[] data)
        {
            if (data == null) return null;
            return Decode(BinaryProcedures.CreateBinaryReader(data, 0));
        }
        public static UserId Decode(BinaryReader reader)
        {
            var r = new UserId();
            r.Flags = reader.ReadByte();
            if ((r.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();

            r._maxCertificateDurationHours = reader.ReadUInt16();
            r.MinimalRequiredRootSignaturesCountInCertificate = reader.ReadByte();
            var rootPublicKeysCount = reader.ReadByte();
            r.RootPublicKeys = new List<byte[]>();
            for (int i = 0; i < rootPublicKeysCount; i++)
                r.RootPublicKeys.Add(reader.ReadBytes(CryptoLibraries.Ed25519PublicKeySize));

            return r;
        }
    }

    public class UserRootPrivateKeys
    {
        const byte FlagsMask_MustBeZero = 0b11100000;

        /// <summary>
        /// are used to sign (intermediate) UserCertificate's
        /// signature of every private key is required
        /// 
        /// the private keys will be separated later, to be stored on separate independent devices
        /// </summary>
        public List<byte[]> ed25519privateKeys; 

        public static void CreateUserId(int numberOfKeyPairs, byte requiredSignaturesCount,
            TimeSpan maxCertificateDuration,
            ICryptoLibrary cryptoLibrary, 
            out UserRootPrivateKeys privateKeys, out UserId publicKeys)
        {
            if (numberOfKeyPairs <= 0 || numberOfKeyPairs > 3) throw new ArgumentException(); // the UDP packets can go over 1000 bytes to transfer a big UsersCertificate
            privateKeys = new UserRootPrivateKeys();
            privateKeys.ed25519privateKeys = new List<byte[]>();
            publicKeys = new UserId();
            publicKeys.MinimalRequiredRootSignaturesCountInCertificate = requiredSignaturesCount;
            publicKeys.MaxCertificateDuration = maxCertificateDuration;
            publicKeys.RootPublicKeys = new List<byte[]>();

            for (int i = 0; i < numberOfKeyPairs; i++)
            {
                var privateKey = cryptoLibrary.GeneratePrivateKeyEd25519();
                var publicKey = cryptoLibrary.GetPublicKeyEd25519(privateKey);
                privateKeys.ed25519privateKeys.Add(privateKey);
                publicKeys.RootPublicKeys.Add(publicKey);
            }
        }

        public string ToCsharpDeclaration
        {
            get
            {
                var r = "ed25519privateKeys = new List<byte[]> {\r\n";
                foreach (var x in ed25519privateKeys)
                    r += $"new byte[] {{ {MiscProcedures.ByteArrayToCsharpDeclaration(x)}}},\r\n";
                r += "}";
                return r;
            }
        }

        public byte[] Encode()
        {
            BinaryProcedures.CreateBinaryWriter(out var ms, out var writer);
            Encode(writer);
            return ms.ToArray();
        }
        public void Encode(BinaryWriter writer)
        {
            byte flags = 0;
            writer.Write(flags);
            writer.Write((byte)ed25519privateKeys.Count);
            foreach (var ed25519privateKey in ed25519privateKeys)
            {
                if (ed25519privateKey.Length != CryptoLibraries.Ed25519PrivateKeySize) throw new Exception("e23473473");
                writer.Write(ed25519privateKey);
            }
        }

        public static UserRootPrivateKeys Decode(byte[] data)
        {
            if (data == null) return null;
            return Decode(BinaryProcedures.CreateBinaryReader(data, 0));
        }
        public static UserRootPrivateKeys Decode(BinaryReader reader)
        {
            var r = new UserRootPrivateKeys();
            byte flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();

            var count = reader.ReadByte();
            r.ed25519privateKeys = new List<byte[]>();
            for (int i = 0; i < count; i++)
                r.ed25519privateKeys.Add(reader.ReadBytes(CryptoLibraries.Ed25519PrivateKeySize));

            return r;
        }

    }

    /// <summary>
    /// a part of UserCertificate
    /// </summary>
    public class UserRootSignature
    {
        public byte RootKeyIndex;

        public byte[] SignatureSalt;
        public const int SignatureSaltSize = 4;

        /// <summary>
        /// signatures by UserRootPrivateKeys[RootKeyIndex].   size of signature is defined by type of root key
        /// 
        /// signs {
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
        // byte Flags; // will include CertificatePublicKey algorithm type = "ed25519" by default
        const byte FlagsMask_MustBeZero = 0b11100000;
        const byte FlagsMask_PrivateKey = 0b00000001;

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
        public void AssertHasPrivateKey()
        {
            if (LocalCertificatePrivateKey == null)
                throw new ArgumentException("certificate does not contain private key");
        }

        public List<UserRootSignature> UserRootSignatures;

        /// <summary>
        /// the procedure verifies certificate that is received from remote party to local party 
        /// throws exception if the certificate is invalid for the specified userId   (the caller knows that certificate came from the userId)
        /// also checks userId - expired or not
        /// </summary>
        public void AssertIsValidNow(ICryptoLibrary cryptoLibrary, UserId userId, DateTime localTimeNowUtc)
        {                      
            if (localTimeNowUtc > ValidToUtc)
                throw new CertificateOutOfDateException($"localTimeNowUtc={localTimeNowUtc} > ValidToUtc={ValidToUtc}");
            if (localTimeNowUtc < ValidFromUtc)
                throw new CertificateOutOfDateException($"localTimeNowUtc={localTimeNowUtc} < ValidFromUtc={ValidFromUtc}");            
            if (ValidToUtc - ValidFromUtc > userId.MaxCertificateDuration)
                throw new BadUserCertificateException($"ValidToUtc - ValidFromUtc = {ValidToUtc - ValidFromUtc} > userId.MaxCertificateDuration={userId.MaxCertificateDuration}");
            

            var validRootSignatureIndexes = new HashSet<int>();
            foreach (var userRootSignature in this.UserRootSignatures)
            {
                var ed25519UserRootPublicKey = userId.RootPublicKeys[userRootSignature.RootKeyIndex];
                var ed25519UserRootSignature = userRootSignature.Signature;

                var signedData = new MemoryStream();
                using (var writer = new BinaryWriter(signedData))
                    WriteSignedFields(writer, userRootSignature);

                if (cryptoLibrary.VerifyEd25519(signedData.ToArray(), ed25519UserRootSignature, ed25519UserRootPublicKey) == false)
                    throw new BadUserCertificateException();

                validRootSignatureIndexes.Add(userRootSignature.RootKeyIndex);
            }

            if (validRootSignatureIndexes.Count < userId.MinimalRequiredRootSignaturesCountInCertificate)
                throw new BadUserCertificateException();
        }
        void WriteSignedFields(BinaryWriter writer, UserRootSignature userRootSignature)
        {
            writer.Write(_validFromUtc32minutes);
            writer.Write(_validToUtc32minutes);
            writer.Write(CertificatePublicKey);
            writer.Write(userRootSignature.SignatureSalt);
        }
        public static UserCertificate GenerateKeyPairsAndSignAtSingleDevice(ICryptoLibrary cryptoLibrary, UserId userId, 
            UserRootPrivateKeys userRootPrivatekeys, DateTime fromUtc, DateTime toUtc)
        {
            if (userId.RootPublicKeys.Count > 255) throw new ArgumentException();
            if (userRootPrivatekeys.ed25519privateKeys.Count != userId.RootPublicKeys.Count) throw new ArgumentException();
            var r = new UserCertificate();         

            r._validFromUtc32minutes = MiscProcedures.DateTimeToUint32minutes(fromUtc);
            r._validToUtc32minutes = MiscProcedures.DateTimeToUint32minutes(toUtc);            
            r.LocalCertificatePrivateKey = cryptoLibrary.GeneratePrivateKeyEd25519();
            r.CertificatePublicKey = cryptoLibrary.GetPublicKeyEd25519(r.LocalCertificatePrivateKey);
            r.UserRootSignatures = new List<UserRootSignature>();
            for (byte i = 0; i < userId.RootPublicKeys.Count; i++)
            {
                var userRootSignature = new UserRootSignature
                {
                    SignatureSalt = cryptoLibrary.GetRandomBytes(UserRootSignature.SignatureSaltSize),
                    RootKeyIndex = i
                };
                var ms = new MemoryStream(); using (var writer = new BinaryWriter(ms)) r.WriteSignedFields(writer, userRootSignature);
                userRootSignature.Signature = cryptoLibrary.SignEd25519(ms.ToArray(), userRootPrivatekeys.ed25519privateKeys[i]);
                r.UserRootSignatures.Add(userRootSignature);  
            }
            return r;
        }
        public void Encode(BinaryWriter writer, bool encodePrivateKey)
        {
            if (encodePrivateKey) AssertHasPrivateKey();
            byte flags = 0;
            if (encodePrivateKey) flags |= FlagsMask_PrivateKey;
            writer.Write(flags);
            writer.Write(_validFromUtc32minutes);
            writer.Write(_validToUtc32minutes);
            if (CertificatePublicKey.Length != CryptoLibraries.Ed25519PublicKeySize) throw new ArgumentException();
            writer.Write(CertificatePublicKey);
            if (encodePrivateKey)
                BinaryProcedures.EncodeByteArray256(writer, LocalCertificatePrivateKey);

            if (UserRootSignatures.Count > 255) throw new ArgumentException();

            writer.Write((byte)UserRootSignatures.Count);
            foreach (var userRootSignature in UserRootSignatures)
            {
                writer.Write(userRootSignature.RootKeyIndex);
                if (userRootSignature.SignatureSalt.Length != UserRootSignature.SignatureSaltSize) throw new ArgumentException();
                writer.Write(userRootSignature.SignatureSalt);
                if (userRootSignature.Signature.Length != CryptoLibraries.Ed25519SignatureSize) throw new ArgumentException();
                if (userRootSignature.Signature.Length >= byte.MaxValue) throw new ArgumentException();
                writer.Write((byte)userRootSignature.Signature.Length);
                writer.Write(userRootSignature.Signature);
            }
        }
        public byte[] Encode(bool encodePrivateKey)
        {
            BinaryProcedures.CreateBinaryWriter(out var ms, out var writer);
            Encode(writer, encodePrivateKey);
            return ms.ToArray();
        }

        public static UserCertificate Decode(BinaryReader reader)
        {
            var r = new UserCertificate();
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
           
            r._validFromUtc32minutes = reader.ReadUInt32();
            r._validToUtc32minutes = reader.ReadUInt32();
            r.CertificatePublicKey = reader.ReadBytes(CryptoLibraries.Ed25519PublicKeySize);
            if ((flags & FlagsMask_PrivateKey) != 0)
                r.LocalCertificatePrivateKey = BinaryProcedures.DecodeByteArray256(reader);

            var userRootSignaturesCount = reader.ReadByte();
            r.UserRootSignatures = new List<UserRootSignature>(userRootSignaturesCount);
            for (int i = 0; i < userRootSignaturesCount; i++)
            {
                var userRootSignature = new UserRootSignature();
                userRootSignature.RootKeyIndex = reader.ReadByte();
                userRootSignature.SignatureSalt = reader.ReadBytes(UserRootSignature.SignatureSaltSize);
                var signatureSize = reader.ReadByte();
                userRootSignature.Signature = reader.ReadBytes(signatureSize);
                r.UserRootSignatures.Add(userRootSignature);
            }
            return r;
        }
        public static UserCertificate Decode(byte[] data)
        {
            if (data == null) return null;
            using var reader = BinaryProcedures.CreateBinaryReader(data, 0);
            return Decode(reader);
        }

        /// <summary>
        /// throws exception if the certificate is invalid for the specified userId   (the caller knows that certificate came from the userId)
        /// </summary>
        /// <param name="userId">is null when it is contact invitation</param>
        public static UserCertificate Decode_AssertIsValidNow(BinaryReader reader, ICryptoLibrary cryptoLibrary, UserId userIdNullable, DateTime localTimeNowUtc)
        {
            var r = Decode(reader); 
            if (userIdNullable != null) r.AssertIsValidNow(cryptoLibrary, userIdNullable, localTimeNowUtc);
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
            userCertificateWithPrivateKey.AssertHasPrivateKey();
           
            var r = new UserCertificateSignature();
            var ms = new MemoryStream(); using (var writer = new BinaryWriter(ms)) writeSignedFields(writer);
            r.CertificateSignature = cryptoLibrary.SignEd25519(
                    ms.ToArray(),
                    userCertificateWithPrivateKey.LocalCertificatePrivateKey);
            return r;
        }
       
        public static UserCertificateSignature DecodeAndVerify(BinaryReader reader, ICryptoLibrary cryptoLibrary, Action<BinaryWriter> writeSignedFields, UserCertificate userCertificateWithPublicKey)
        {
            var r = Decode(reader);
            if (!r.Verify(cryptoLibrary, writeSignedFields, userCertificateWithPublicKey)) throw new BadSignatureException("bad user cert 1235");
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
        internal static UserCertificateSignature Decode(BinaryReader reader)
        {
            var r = new UserCertificateSignature();
            r.Flags = reader.ReadByte();
            if ((r.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            r.CertificateSignature = reader.ReadBytes(CryptoLibraries.Ed25519SignatureSize);
            return r;
        }
    }
}
