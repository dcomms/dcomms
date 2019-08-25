using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace Dcomms.DRP
{
    public class RegistrationPublicKey
    {
        byte ReservedFlagsMustBeZero; // will include "type" = "ed25519 by default" // will include "type of distance metric"
        public byte[] ed25519publicKey;

        public void Encode(BinaryWriter writer)
        {
            writer.Write(ReservedFlagsMustBeZero);
            if (ed25519publicKey.Length != CryptoLibraries.Ed25519PublicKeySize) throw new ArgumentException();
            writer.Write(ed25519publicKey);
        }
        public static RegistrationPublicKey Decode(BinaryReader reader)
        {
            var r = new RegistrationPublicKey();
            r.ReservedFlagsMustBeZero = reader.ReadByte();
            r.ed25519publicKey = reader.ReadBytes(CryptoLibraries.Ed25519PublicKeySize);
            return r;
        }
        public override bool Equals(object obj)
        {
            var obj2 = (RegistrationPublicKey)obj;
            return obj2.ReservedFlagsMustBeZero == this.ReservedFlagsMustBeZero && MiscProcedures.EqualByteArrays(obj2.ed25519publicKey, this.ed25519publicKey);
        }
        public override int GetHashCode()
        {
            return MiscProcedures.GetArrayHashCode(ed25519publicKey) ^ ReservedFlagsMustBeZero;
        }
        public RegistrationPublicKeyDistance GetDistanceTo(RegistrationPublicKey another) => new RegistrationPublicKeyDistance(this, another);
       
    }
    public class RegistrationPublicKeyDistance
    {
        int _distance; // 32 bytes of reg. public key: split into 16 dimensions of 2 bytes //   manhattan distance: https://en.wikipedia.org/wiki/Taxicab_geometry
        public unsafe RegistrationPublicKeyDistance(RegistrationPublicKey rpk1, RegistrationPublicKey rpk2)
        {
            if (rpk1.ed25519publicKey.Length != rpk2.ed25519publicKey.Length) throw new ArgumentException();
            _distance = 0;
            fixed (byte* rpk1a = rpk1.ed25519publicKey, rpk2a = rpk2.ed25519publicKey)                
            {
                short* rpk1aPtr = (short*)rpk1a, rpk2aPtr = (short*)rpk2a;
                int l = rpk1.ed25519publicKey.Length;
                for (int i = 0; i < l; i++, rpk1aPtr++, rpk2aPtr++)
                    _distance += Math.Abs(unchecked(*rpk1aPtr - *rpk2aPtr));
            }           
        }
        public bool IsGreaterThan(RegistrationPublicKeyDistance another)
        {
            return this._distance > another._distance;
        }
    }


    public class RegistrationPrivateKey
    {
        public byte[] ed25519privateKey;
    }
    public class RegistrationSignature
    {
        byte ReservedFlagsMustBeZero; // will include "type" = "ed25519 by default"
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
            writer.Write(ReservedFlagsMustBeZero);
            if (ed25519signature.Length != CryptoLibraries.Ed25519SignatureSize) throw new ArgumentException();
            writer.Write(ed25519signature);
        }
        public static RegistrationSignature Decode(BinaryReader reader)
        {
            var r = new RegistrationSignature();
            r.ReservedFlagsMustBeZero = reader.ReadByte();
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
            if (cryptoLibrary.VerifyEd25519(signedData.ToArray(), ed25519signature, publicKey.ed25519publicKey) == false)
                return false;
            return true;
        }
    }
    public class EcdhPublicKey
    {
        byte ReservedFlagsMustBeZero; // will include "type" = "ec25519 ecdh" by default
        public byte[] ecdh25519PublicKey; 

        public void Encode(BinaryWriter writer)
        {
            writer.Write(ReservedFlagsMustBeZero);
            if (ecdh25519PublicKey.Length != CryptoLibraries.Ecdh25519PublicKeySize) throw new ArgumentException();
            writer.Write(ecdh25519PublicKey);
        }
        public static EcdhPublicKey Decode(BinaryReader reader)
        {
            var r = new EcdhPublicKey();
            r.ReservedFlagsMustBeZero = reader.ReadByte();
            r.ecdh25519PublicKey = reader.ReadBytes(CryptoLibraries.Ecdh25519PublicKeySize);
            // todo: check if it is valid point on curve  - do we really need to check it?
            return r;
        }
    }
  
    public class HMAC
    {
        byte ReservedFlagsMustBeZero; // will include "type" = "ecdhe->KDF->sharedkey -> +plainText -> sha256" by default
        public byte[] hmacSha256; // 32 bytes for hmac_sha256
        public void Encode(BinaryWriter writer)
        {
            writer.Write(ReservedFlagsMustBeZero);
            if (hmacSha256.Length != 32) throw new ArgumentException();
            writer.Write(hmacSha256);
        }

        public static HMAC Decode(BinaryReader reader)
        {
            var r = new HMAC();
            r.ReservedFlagsMustBeZero = reader.ReadByte();
            r.hmacSha256 = reader.ReadBytes(32);
            return r;
        }
        public override bool Equals(object obj)
        {
            var obj2 = (HMAC)obj;
            if (obj2.ReservedFlagsMustBeZero != this.ReservedFlagsMustBeZero) return false;
            return MiscProcedures.EqualByteArrays(obj2.hmacSha256, this.hmacSha256);
        }
        public override string ToString() => MiscProcedures.ByteArrayToString(hmacSha256);
    }

}
