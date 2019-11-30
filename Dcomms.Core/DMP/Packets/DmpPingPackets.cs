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
        const byte Flags_PublicEcdheKeySet = 0b00000001;
        const byte FlagsMask_MustBeZero = 0b11110000;
        public EcdhPublicKey PublicEcdheKey; // nullable
        public HMAC PingPongHMAC; 

        public void GetSignedFieldsForPingPongHMAC(BinaryWriter writer)
        {
            writer.Write((byte)PacketTypes.DmpPing);
            DirectChannelToken32.Encode(writer);
            writer.Write(PingRequestId32);            
            if (PublicEcdheKey != null)
            {
                PublicEcdheKey.Encode(writer);
            }
        }
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            
            writer.Write((byte)PacketTypes.DmpPing);
            DirectChannelToken32.Encode(writer);
            writer.Write(PingRequestId32);

            byte flags = 0;
            if (PublicEcdheKey != null)
                flags |= Flags_PublicEcdheKeySet;
            writer.Write(flags);

            if (PublicEcdheKey != null)
            {
                PublicEcdheKey.Encode(writer);
            }


            PingPongHMAC.Encode(writer);
            return ms.ToArray();
        }

        public static ushort DecodeDcToken16(byte[] udpData)
        { // first byte is packet type. then 4 bytes are DirectChannelToken32
            return (ushort)(udpData[1] | (udpData[2] << 8));
        }

        public static DmpPingPacket DecodeAndVerify(byte[] udpData, InviteSession session)
        {
            var reader = PacketProcedures.CreateBinaryReader(udpData, 1);

            var r = new DmpPingPacket();
            r.DirectChannelToken32 = DirectChannelToken32.Decode(reader);
            r.PingRequestId32 = reader.ReadUInt32();

            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();

            if ((flags & Flags_PublicEcdheKeySet) != 0)
            {
                r.PublicEcdheKey = EcdhPublicKey.Decode(reader);
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
        const byte Flags_PublicEcdheKeySet = 0b00000001;
        const byte FlagsMask_MustBeZero = 0b11110000;

        public EcdhPublicKey PublicEcdheKey; // nullable

        public HMAC PingPongHMAC; 

        public static DmpPongPacket Decode(byte[] udpData)
        {
            var reader = PacketProcedures.CreateBinaryReader(udpData, 1);
            var r = new DmpPongPacket();
            r.DirectChannelToken32 = DirectChannelToken32.Decode(reader);
            r.PingRequestId32 = reader.ReadUInt32();
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();

            if ((flags & Flags_PublicEcdheKeySet) != 0)
            {
                r.PublicEcdheKey = EcdhPublicKey.Decode(reader);
            }

            r.PingPongHMAC = HMAC.Decode(reader);
                      
            return r;
        }
        public static ushort DecodeDcToken16(byte[] udpData)
        { // first byte is packet type. then 4 bytes are DirectChannelToken32
            return (ushort)(udpData[1] | (udpData[2] << 8));
        }

        public static LowLevelUdpResponseScanner GetScanner(DirectChannelToken32 senderToken32, uint pingRequestId32, InviteSession session)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            GetHeaderFields(w, senderToken32, pingRequestId32);
            return new LowLevelUdpResponseScanner
            {
                ResponseFirstBytes = ms.ToArray(),
                OptionalFilter = (udpData) =>
                {
                    var pong = Decode(udpData);
                    if (pong.PingPongHMAC.Equals(
                        session.GetPingPongHMAC(pong.GetSignedFieldsForPingPongHMAC)
                        ) == false)
                        return false;
                    return true;
                }
            };
        }
        static void GetHeaderFields(BinaryWriter writer, DirectChannelToken32 directChannelToken32, uint pingRequestId32)
        {
            writer.Write((byte)PacketTypes.DmpPong);
            directChannelToken32.Encode(writer);
            writer.Write(pingRequestId32);
        }

        public void GetSignedFieldsForPingPongHMAC(BinaryWriter writer)
        {
            GetHeaderFields(writer, DirectChannelToken32, PingRequestId32);
            if (PublicEcdheKey != null)
            {
                PublicEcdheKey.Encode(writer);
            }
        }
                   
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            GetHeaderFields(writer, DirectChannelToken32, PingRequestId32);           
            byte flags = 0;
            if (PublicEcdheKey != null)
                flags |= Flags_PublicEcdheKeySet;
            writer.Write(flags);

            if (PublicEcdheKey != null)
            {
                PublicEcdheKey.Encode(writer);
            }

            PingPongHMAC.Encode(writer);
            return ms.ToArray();
        }
    }
}
