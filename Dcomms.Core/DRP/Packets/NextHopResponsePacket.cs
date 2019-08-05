using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{


    /// <summary>
    /// is sent from next hop to previous hop, when the next hop receives some packet from neighbor, or from registering peer (RP->A).
    /// stops UDP retransmission of a request packet
    /// </summary>
    class NextHopResponsePacket
    {
        public static byte Flag_RPtoA = 0x01; // set if packet is transmitted from RP to A, is zero otherwise

        public RemotePeerToken32 SenderToken32; // is not transmitted in RP->A packet
        public RegistrationPublicKey RequesterPublicKey_RequestID;
        public NextHopResponseCode StatusCode;
        /// <summary>
        /// signature of sender neighbor peer
        /// is NULL for RP->A packet
        /// uses common secret of neighbors within P2P connection
        /// </summary>
        public HMAC SenderHMAC;

        public byte[] Encode(byte flags)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpPacketType.NextHopResponsePacket);
            writer.Write(flags);
            if ((flags & Flag_RPtoA) == 0) SenderToken32.Encode(writer);
            RequesterPublicKey_RequestID.Encode(writer);
            writer.Write((byte)StatusCode);
            if ((flags & Flag_RPtoA) == 0) SenderHMAC.Encode(writer);
            return ms.ToArray();
        }
        /// <param name="reader">is positioned after first byte = packet type</param>
        public NextHopResponsePacket(BinaryReader reader)
        {
            var flags = reader.ReadByte();
            if ((flags & Flag_RPtoA) == 0) SenderToken32 = RemotePeerToken32.Decode(reader);
            RequesterPublicKey_RequestID = RegistrationPublicKey.Decode(reader);
            StatusCode = (NextHopResponseCode)reader.ReadByte();
            if ((flags & Flag_RPtoA) == 0) SenderHMAC = HMAC.Decode(reader);
        }
    }
    enum NextHopResponseCode
    {
        received, // is sent to previous hop immediately when packet is proxied, to avoid retransmissions      
        rejected_overloaded,
        rejected_rateExceeded, // anti-ddos
    }
}
