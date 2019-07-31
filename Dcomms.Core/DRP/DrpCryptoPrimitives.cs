using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP
{
    public class RegistrationPublicKey
    {
        byte ReservedFlagsMustBeZero; // will include "type" = "ed25519 by default"
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
    }
    public class RegistrationPrivateKey
    {
        public byte[] ed25519privateKey;
    }
    public class RegistrationSignature
    {
        byte ReservedFlagsMustBeZero; // will include "type" = "ed25519 by default"
        public byte[] ed25519signature;
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
    }
    class SecretHeyForHmac
    {
        public byte[] secretkey; // is same at 2 neighbor peers
    }
    class HMAC
    {
        byte ReservedFlagsMustBeZero; // will include "type" = "sha256 by default"
        public byte[] hmac; // 32 bytes for hmac_sha256
        public void Encode(BinaryWriter writer)
        {
            writer.Write(ReservedFlagsMustBeZero);
            if (hmac.Length != 32) throw new ArgumentException();
            writer.Write(hmac);
        }

        public static HMAC Decode(BinaryReader reader)
        {
            var r = new HMAC();
            r.ReservedFlagsMustBeZero = reader.ReadByte();
            r.hmac = reader.ReadBytes(32);
            return r;
        }
    }

}
