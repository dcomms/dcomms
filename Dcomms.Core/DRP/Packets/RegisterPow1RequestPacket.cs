using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{
    /// <summary>
    /// A = requester
    /// EP = entry server, proxy peer
    /// is sent from A to EP when A connects to the P2P network
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
        /// <summary>
        /// must be copied by EP into RegisterPow1ResponsePacket
        /// </summary>
        public uint Pow1RequestId;

        public RegisterPow1RequestPacket()
        {
        }
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            Encode(writer);
            return ms.ToArray();
        }
        void Encode(BinaryWriter writer)
        {
            writer.Write((byte)DrpPacketType.RegisterPow1Request);
            writer.Write(ReservedFlagsMustBeZero);
            writer.Write(Timestamp32S);
            if (ProofOfWork1.Length != 64) throw new ArgumentException();
            writer.Write(ProofOfWork1);
            writer.Write(Pow1RequestId);
        }

        /// <param name="reader">positioned after first byte = packet type</param>
        public RegisterPow1RequestPacket(byte[] originalPacketUdpPayload)
        {
            var reader = PacketProcedures.CreateBinaryReader(originalPacketUdpPayload, 1);
            ReservedFlagsMustBeZero = reader.ReadByte();
            Timestamp32S = reader.ReadUInt32();
            ProofOfWork1 = reader.ReadBytes(64);
            Pow1RequestId = reader.ReadUInt32();
        }
    }
}
