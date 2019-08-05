using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{
    /// <summary>
    /// A = requester
    /// RP = rendezvous server, proxy peer
    /// is sent from A to RP when A connects to the P2P network
    /// protects system against IP spoofing
    /// </summary>
    class RegisterPow1RequestPacket
    {
        public byte ReservedFlagsMustBeZero; // will include PoW type
        public uint Timestamp32S; // seconds since 2019-01-01 UTC, 32 bits are enough for 136 years

        /// <summary>
        /// default PoW type: 64 bytes
        /// sha512(Timestamp32S|ProofOfWork1|requesterPublicIp) has byte[6]=7 
        /// todo: consider PoW's based on CryptoNight, argon2, bcrypt, scrypt:  slow on GPUs.   the SHA512 is fast on GPUs, that could be used by DDoS attackers
        /// </summary>
        public byte[] ProofOfWork1;

        public RegisterPow1RequestPacket()
        {
        }
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            Encode(writer);
            return ms.ToArray();
        }
        public void Encode(BinaryWriter writer)
        {
            writer.Write((byte)DrpPacketType.RegisterPow1RequestPacket);
            writer.Write(ReservedFlagsMustBeZero);
            writer.Write(Timestamp32S);
            if (ProofOfWork1.Length != 64) throw new ArgumentException();
            writer.Write(ProofOfWork1);
        }
        public readonly byte[] OriginalPacketPayload;

        /// <param name="reader">positioned after first byte = packet type</param>
        public RegisterPow1RequestPacket(BinaryReader reader, byte[] originalPacketPayload)
        {
            OriginalPacketPayload = originalPacketPayload;
            ReservedFlagsMustBeZero = reader.ReadByte();
            Timestamp32S = reader.ReadUInt32();
            ProofOfWork1 = reader.ReadBytes(64);
        }
    }
}
