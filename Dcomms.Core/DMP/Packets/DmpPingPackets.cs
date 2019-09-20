using Dcomms.Cryptography;
using Dcomms.DRP;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DMP.Packets
{
    public class DmpPingPacket
    {
        /// <summary>
        /// comes from session description
        /// </summary>
        public DirectChannelToken32 DirectChannelToken32;
        public uint PingRequestId32; // is used to avoid mismatch between delyed responses and requests // is used as salt also
     //   public byte Flags;
        const byte Flags_PublicEcdheKeysSet = 0b00000001;
        const byte FlagsMask_MustBeZero = 0b11110000;
        public EcdhPublicKey PublicEcdheKeyA; // nullable
        public EcdhPublicKey PublicEcdheKeyE; // nullable
        public HMAC PingPongHMAC; 

        public void GetSignedFieldsForPingPongHMAC(BinaryWriter writer)
        {
            writer.Write((byte)DrpDmpPacketTypes.DmpPing);
            DirectChannelToken32.Encode(writer);
            writer.Write(PingRequestId32);            
            if (PublicEcdheKeyA != null)
            {
                PublicEcdheKeyA.Encode(writer);
                PublicEcdheKeyE.Encode(writer);
            }
        }
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            
            writer.Write((byte)DrpDmpPacketTypes.DmpPing);
            DirectChannelToken32.Encode(writer);
            writer.Write(PingRequestId32);

            byte flags = 0;
            if (PublicEcdheKeyA != null)
                flags |= Flags_PublicEcdheKeysSet;
            writer.Write(flags);

            if (PublicEcdheKeyA != null)
            {
                PublicEcdheKeyA.Encode(writer);
                PublicEcdheKeyE.Encode(writer);
            }


            PingPongHMAC.Encode(writer);
            return ms.ToArray();
        }

        public static ushort DecodeDcToken16(byte[] udpPayloadData)
        { // first byte is packet type. then 4 bytes are DirectChannelToken32
            return (ushort)(udpPayloadData[1] | (udpPayloadData[2] << 8));
        }

        public static DmpPingPacket DecodeAndVerify(byte[] udpPayloadData, Session session)
        {
            var reader = PacketProcedures.CreateBinaryReader(udpPayloadData, 1);

            var r = new DmpPingPacket();
            r.DirectChannelToken32 = DirectChannelToken32.Decode(reader);
            r.PingRequestId32 = reader.ReadUInt32();

            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();

            if ((flags & Flags_PublicEcdheKeysSet) != 0)
            {
                r.PublicEcdheKeyA = EcdhPublicKey.Decode(reader);
                r.PublicEcdheKeyE = EcdhPublicKey.Decode(reader);
            }

            r.PingPongHMAC = HMAC.Decode(reader);

            // verify DirectChannelToken32
            if (!r.DirectChannelToken32.Equals(session.LocalSessionDescription.DirectChannelToken32))
                throw new BadSignatureException();

            // verify PingPongHMAC
            if (r.PingPongHMAC.Equals(
                session.GetPingPongHMAC(r.GetSignedFieldsForPingPongHMAC)
                ) == false)
                throw new BadSignatureException();

            return r;
        }
    }
    public class DmpPongPacket
    {
        /// <summary>
        /// comes from session description
        /// </summary>
        public DirectChannelToken32 DirectChannelToken32;
        public uint PingRequestId32;  // must match to request
        const byte Flags_PublicEcdheKeysSet = 0b00000001;
        const byte FlagsMask_MustBeZero = 0b11110000;

        public EcdhPublicKey PublicEcdheKeyA; // nullable
        public EcdhPublicKey PublicEcdheKeyE; // nullable

        public HMAC PingPongHMAC; 

        /// <param name="reader">is positioned after first byte = packet type</param>
        public static DmpPongPacket DecodeAndVerify(ICryptoLibrary cryptoLibrary,
            byte[] udpPayloadData, DmpPingPacket pingRequestPacketToCheckRequestId32, 
            Session session
            )
        {
            var reader = PacketProcedures.CreateBinaryReader(udpPayloadData, 1);
            var r = new DmpPongPacket();
            r.DirectChannelToken32 = DirectChannelToken32.Decode(reader);
            r.PingRequestId32 = reader.ReadUInt32();
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();

            if ((flags & Flags_PublicEcdheKeysSet) != 0)
            {
                r.PublicEcdheKeyA = EcdhPublicKey.Decode(reader);
                r.PublicEcdheKeyE = EcdhPublicKey.Decode(reader);
            }

            r.PingPongHMAC = HMAC.Decode(reader);


            // verify PingRequestId32
            if (r.PingRequestId32 != pingRequestPacketToCheckRequestId32.PingRequestId32)
                throw new UnmatchedFieldsException();

            // verify DirectChannelToken32
            if (!r.DirectChannelToken32.Equals(session.LocalSessionDescription.DirectChannelToken32))
                throw new UnmatchedFieldsException();

            // verify PingPongHMAC
            var expectedHMAC = session.GetPingPongHMAC(r.GetSignedFieldsForPingPongHMAC);
            if (r.PingPongHMAC.Equals(expectedHMAC) == false)
            {
             //   connectedPeerWhoSentTheResponse.Engine.WriteToLog_p2p_detail(connectedPeerWhoSentTheResponse, $"incorrect sender HMAC in ping response: {r.NeighborHMAC}. expected: {expectedHMAC}");
                throw new BadSignatureException();
            }
          
            return r;
        }
        public static ushort DecodeDcToken16(byte[] udpPayloadData)
        { // first byte is packet type. then 4 bytes are DirectChannelToken32
            return (ushort)(udpPayloadData[1] | (udpPayloadData[2] << 8));
        }

        public static LowLevelUdpResponseScanner GetScanner(DirectChannelToken32 senderToken32, uint pingRequestId32)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            GetHeaderFields(w, senderToken32, pingRequestId32);
            return new LowLevelUdpResponseScanner { ResponseFirstBytes = ms.ToArray() };
        }
        static void GetHeaderFields(BinaryWriter writer, DirectChannelToken32 directChannelToken32, uint pingRequestId32)
        {
            writer.Write((byte)DrpDmpPacketTypes.DmpPong);
            directChannelToken32.Encode(writer);
            writer.Write(pingRequestId32);
        }

        public void GetSignedFieldsForPingPongHMAC(BinaryWriter writer)
        {
            GetHeaderFields(writer, DirectChannelToken32, PingRequestId32);
            if (PublicEcdheKeyA != null)
            {
                PublicEcdheKeyA.Encode(writer);
                PublicEcdheKeyE.Encode(writer);
            }
        }
                   
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            GetHeaderFields(writer, DirectChannelToken32, PingRequestId32);           
            byte flags = 0;
            if (PublicEcdheKeyA != null)
                flags |= Flags_PublicEcdheKeysSet;
            writer.Write(flags);

            if (PublicEcdheKeyA != null)
            {
                PublicEcdheKeyA.Encode(writer);
                PublicEcdheKeyE.Encode(writer);
            }

            PingPongHMAC.Encode(writer);
            return ms.ToArray();
        }
    }
}
