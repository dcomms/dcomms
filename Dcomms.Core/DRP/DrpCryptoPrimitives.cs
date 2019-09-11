using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Dcomms.DRP
{
    public class RegistrationPublicKey
    {
        //  byte Flags; // will include "type" = "ed25519 by default" // will include "type of distance metric"
        const byte FlagsMask_MustBeZero = 0b11110000;
        public byte[] Ed25519publicKey;
        public byte[] CachedEd25519publicKeySha256;
       
        public RegistrationPublicKey(byte[] ed25519publicKey)
        {
            Ed25519publicKey = ed25519publicKey;
        }

        public void Encode(BinaryWriter writer)
        {
            byte flags = 0;
            writer.Write(flags);
            if (Ed25519publicKey.Length != CryptoLibraries.Ed25519PublicKeySize) throw new ArgumentException();
            writer.Write(Ed25519publicKey);
        }
        public static RegistrationPublicKey Decode(BinaryReader reader)
        {
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            var r = new RegistrationPublicKey(reader.ReadBytes(CryptoLibraries.Ed25519PublicKeySize));        
            return r;
        }
        public override bool Equals(object obj)
        {
            var obj2 = (RegistrationPublicKey)obj;
            return MiscProcedures.EqualByteArrays(obj2.Ed25519publicKey, this.Ed25519publicKey);
        }
        public override int GetHashCode()
        {
            return MiscProcedures.GetArrayHashCode(Ed25519publicKey);
        }
        public override string ToString()
        {
            return MiscProcedures.ByteArrayToString(Ed25519publicKey);
        }
        public RegistrationPublicKeyDistance GetDistanceTo(ICryptoLibrary cryptoLibrary, RegistrationPublicKey another) => new RegistrationPublicKeyDistance(cryptoLibrary, this, another);
       
    }
    public class RegistrationPublicKeyDistance
    {
        Int64 _distance; // 32 bytes of reg. public key: split into 16 dimensions of 2 bytes //   euclidean distance
        public unsafe RegistrationPublicKeyDistance(ICryptoLibrary cryptoLibrary, RegistrationPublicKey rpk1, RegistrationPublicKey rpk2)
        {
            if (rpk1.CachedEd25519publicKeySha256 == null) rpk1.CachedEd25519publicKeySha256 = cryptoLibrary.GetHashSHA256(rpk1.Ed25519publicKey);
            var rpk1_ed25519publicKey_sha256 = rpk1.CachedEd25519publicKeySha256;
            if (rpk2.CachedEd25519publicKeySha256 == null) rpk2.CachedEd25519publicKeySha256 = cryptoLibrary.GetHashSHA256(rpk2.Ed25519publicKey);
            var rpk2_ed25519publicKey_sha256 = rpk2.CachedEd25519publicKeySha256;            

            if (rpk1_ed25519publicKey_sha256.Length != rpk2_ed25519publicKey_sha256.Length) throw new ArgumentException();
            _distance = 0;
            fixed (byte* rpk1a = rpk1_ed25519publicKey_sha256, rpk2a = rpk2_ed25519publicKey_sha256)                
            {
                short* rpk1aPtr = (short*)rpk1a, rpk2aPtr = (short*)rpk2a;
                int l = rpk1_ed25519publicKey_sha256.Length / 2;
                for (int i = 0; i < l; i++, rpk1aPtr++, rpk2aPtr++)
                {
                    int x = Math.Abs(unchecked(*rpk1aPtr - *rpk2aPtr));
                    _distance += (x * x);
                }
            }           
        }
        public bool IsGreaterThan(RegistrationPublicKeyDistance another)
        {
            return this._distance > another._distance;
        }
        public override string ToString() => ((float)_distance).ToString("E02");
    }


    public class RegistrationPrivateKey
    {
        public byte[] ed25519privateKey;
    }
    public class RegistrationSignature
    {
        byte Flags; // will include "type" = "ed25519 by default"
        const byte FlagsMask_MustBeZero = 0b11110000;
        public byte[] ed25519signature;

        public static RegistrationSignature Sign(ICryptoLibrary cryptoLibrary, Action<BinaryWriter> writeSignedFields, RegistrationPrivateKey privateKey)
        {
            var r = new RegistrationSignature();
            var ms = new MemoryStream(); using (var writer = new BinaryWriter(ms)) writeSignedFields(writer);
            r.ed25519signature = cryptoLibrary.SignEd25519(
                    ms.ToArray(),
                    privateKey.ed25519privateKey);
            return r;
        }


        public void Encode(BinaryWriter writer)
        {
            writer.Write(Flags);
            if (ed25519signature.Length != CryptoLibraries.Ed25519SignatureSize) throw new ArgumentException();
            writer.Write(ed25519signature);
        }
        public static RegistrationSignature Decode(BinaryReader reader)
        {
            var r = new RegistrationSignature();
            r.Flags = reader.ReadByte();
            if ((r.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            r.ed25519signature = reader.ReadBytes(CryptoLibraries.Ed25519SignatureSize);
            return r;
        }
        public static RegistrationSignature DecodeAndVerify(BinaryReader reader, ICryptoLibrary cryptoLibrary, Action<BinaryWriter> writeSignedFields, RegistrationPublicKey publicKey)
        {
            var r = Decode(reader);  
            if (!r.Verify(cryptoLibrary, writeSignedFields, publicKey)) throw new BadSignatureException();     
            return r;
        }
        public bool Verify(ICryptoLibrary cryptoLibrary, Action<BinaryWriter> writeSignedFields, RegistrationPublicKey publicKey)
        {
            var signedData = new MemoryStream();
            using (var writer = new BinaryWriter(signedData))
                writeSignedFields(writer);
            if (cryptoLibrary.VerifyEd25519(signedData.ToArray(), ed25519signature, publicKey.Ed25519publicKey) == false)
                return false;
            return true;
        }
    }
    public class EcdhPublicKey
    {
        byte Flags; // will include "type" = "ec25519 ecdh" by default
        const byte FlagsMask_MustBeZero = 0b11110000;
       
        public byte[] ecdh25519PublicKey; 

        public void Encode(BinaryWriter writer)
        {
            writer.Write(Flags);
            if (ecdh25519PublicKey.Length != CryptoLibraries.Ecdh25519PublicKeySize) throw new ArgumentException();
            writer.Write(ecdh25519PublicKey);
        }
        public static EcdhPublicKey Decode(BinaryReader reader)
        {
            var r = new EcdhPublicKey();
            r.Flags = reader.ReadByte();
            if ((r.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            r.ecdh25519PublicKey = reader.ReadBytes(CryptoLibraries.Ecdh25519PublicKeySize);
            // todo: check if it is valid point on curve  - do we really need to check it?
            return r;
        }
    }
  
    public class HMAC
    {
        byte Flags; // will include "type" = "ecdhe->KDF->sharedkey -> +plainText -> sha256" by default
        const byte FlagsMask_MustBeZero = 0b11110000;
        public byte[] hmacSha256; // 32 bytes for hmac_sha256
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Flags);
            if (hmacSha256.Length != 32) throw new ArgumentException();
            writer.Write(hmacSha256);
        }

        public static HMAC Decode(BinaryReader reader)
        {
            var r = new HMAC();
            r.Flags = reader.ReadByte();
            if ((r.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            r.hmacSha256 = reader.ReadBytes(32);
            return r;
        }
        public override bool Equals(object obj)
        {
            var obj2 = (HMAC)obj;
            if (obj2.Flags != this.Flags) return false;
            return MiscProcedures.EqualByteArrays(obj2.hmacSha256, this.hmacSha256);
        }
        public override string ToString() => MiscProcedures.ByteArrayToString(hmacSha256);
    }



     

}
