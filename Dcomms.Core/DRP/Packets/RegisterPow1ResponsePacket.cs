using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{
    /// <summary>
    /// verifies UDP/IP path from EP to A, check UDP.sourceIP
    /// can be used for UDP reflection attacks
    /// </summary>
    class RegisterPow1ResponsePacket
    {
        public uint Pow1RequestId;
        public byte Flags;
        const byte FlagsMask_MustBeZero = 0b11000000;
        public RegisterPow1ResponseStatusCode StatusCode;
        public byte[] ProofOfWork2Request; // 16 bytes

        public static LowLevelUdpResponseScanner GetScanner(uint pow1RequestId)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            GetHeaderBytes(writer, pow1RequestId);
            return new LowLevelUdpResponseScanner { ResponseFirstBytes = ms.ToArray() };
        }
        static void GetHeaderBytes(BinaryWriter writer, uint pow1RequestId)
        {
            writer.Write((byte)DrpPacketType.RegisterPow1Response);
            writer.Write(pow1RequestId);
        }
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            GetHeaderBytes(writer, Pow1RequestId);
            writer.Write(Flags);
            writer.Write((byte)StatusCode);
            if (StatusCode == RegisterPow1ResponseStatusCode.succeeded_Pow2Challenge)
                writer.Write(ProofOfWork2Request);
            return ms.ToArray();
        }
        public RegisterPow1ResponsePacket()
        {

        }
        public RegisterPow1ResponsePacket(byte[] rpPow1ResponsePacketData)
        {
            var reader = PacketProcedures.CreateBinaryReader(rpPow1ResponsePacketData, 1);
            Pow1RequestId = reader.ReadUInt32();
            Flags = reader.ReadByte();
            if ((Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            StatusCode = (RegisterPow1ResponseStatusCode)reader.ReadByte();
            if (StatusCode == RegisterPow1ResponseStatusCode.succeeded_Pow2Challenge)
            {
                ProofOfWork2Request = reader.ReadBytes(16);
            }
        }
    }
    enum RegisterPow1ResponseStatusCode
    {
        succeeded_Pow2Challenge = 0,

        rejected = 1, // is sent if peer in "developer" mode only
        rejected_badtimestamp = 2, // is sent if peer in "developer" mode only (???) peer is responsible for his clock, using 3rd party time servers
        rejected_badPublicIp = 3, // is sent if peer in "developer" mode only
        rejected_tryagainRightNowWithThisServer = 4 // is sent if peer in "developer" mode only
        // also: ignored
    }
}
