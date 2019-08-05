using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{
    /// <summary>
    /// verifies UDP/IP path from RP to A, check UDP.sourceIP
    /// can be used for UDP reflection attacks
    /// </summary>
    class RegisterPow1ResponsePacket
    {
        public byte ReservedFlagsMustBeZero;
        public RegisterPow1ResponseStatusCode StatusCode;
        public byte[] ProofOfWork2Request; // 16 bytes

        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            writer.Write((byte)DrpPacketType.RegisterPow1ResponsePacket);
            writer.Write(ReservedFlagsMustBeZero);
            writer.Write((byte)StatusCode);
            if (StatusCode == RegisterPow1ResponseStatusCode.succeeded_Pow2Challenge)
                writer.Write(ProofOfWork2Request);
            return ms.ToArray();
        }
        /// <param name="reader">is positioned after first byte = packet type</param>
        public RegisterPow1ResponsePacket(BinaryReader reader)
        {
            ReservedFlagsMustBeZero = reader.ReadByte();
            StatusCode = (RegisterPow1ResponseStatusCode)reader.ReadByte();
            if (StatusCode == RegisterPow1ResponseStatusCode.succeeded_Pow2Challenge)
            {
                ProofOfWork2Request = reader.ReadBytes(16);
            }
        }
    }
    enum RegisterPow1ResponseStatusCode
    {
        succeeded_Pow2Challenge,

        rejected, // is sent if peer in "developer" mode only
        rejected_badtimestamp, // is sent if peer in "developer" mode only (???) peer is responsible for his clock, using 3rd party time servers
        rejected_badPublicIp // is sent if peer in "developer" mode only
        // also: ignored
    }
}
