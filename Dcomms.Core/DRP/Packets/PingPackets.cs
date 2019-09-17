using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{
    public class PingPacket
    {
        /// <summary>
        /// comes from ConnectionToNeighbor.RemoteNeighborToken32
        /// </summary>
        public NeighborToken32 NeighborToken32;
        public const byte Flags_RegistrationConfirmationSignatureRequested = 0x01;
        public byte Flags;
        const byte FlagsMask_MustBeZero = 0b11110000;
        public uint PingRequestId32; // is used to avoid mismatch between delyed responses and requests // is used as salt also
        public float? MaxRxInviteRateRps;   // zero means NULL // signal from sender "how much I can receive via this p2p connection"
        public float? MaxRxRegisterRateRps; // zero means NULL // signal from sender "how much I can receive via this p2p connection"
        public HMAC NeighborHMAC; // signs fields { DrpPacketType.PingRequestPacket,NeighborToken32,Flags,PingRequestId32,MaxRxInviteRateRps,MaxRxRegisterRateRps  }, to authenticate the request

        static ushort RpsToUint16(float? rps) // resolution=0.01 RPS    max value=0.65K RPS
        {
            return (ushort)Math.Round(Math.Min(65535, (rps ?? 0) * 100));
        }
        static float? RpsFromUint16(ushort v)
        {
            return v != 0 ? (float?)((float)v * 0.01) : null;
        }
        public void GetSignedFieldsForNeighborHMAC(BinaryWriter writer)
        {
            writer.Write((byte)DrpPacketType.Ping);
            NeighborToken32.Encode(writer);
            writer.Write(Flags);
            writer.Write(PingRequestId32);
            writer.Write(RpsToUint16(MaxRxInviteRateRps));
            writer.Write(RpsToUint16(MaxRxRegisterRateRps));
        }
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);            
            GetSignedFieldsForNeighborHMAC(writer);
            NeighborHMAC.Encode(writer);
            return ms.ToArray();
        }

        public static ushort DecodeToken16FromUdpPayloadData(byte[] udpPayloadData)
        { // first byte is packet type. then 4 bytes are NeighborToken32
            return (ushort)(udpPayloadData[1] | (udpPayloadData[2] << 8));
        }

        public static PingPacket DecodeAndVerify(byte[] udpPayloadData, ConnectionToNeighbor connectedPeerWhoSentTheRequest)
        {
            var reader = PacketProcedures.CreateBinaryReader(udpPayloadData, 1);

            var r = new PingPacket();
            r.NeighborToken32 = NeighborToken32.Decode(reader);
            r.Flags = reader.ReadByte();
            if ((r.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            r.PingRequestId32 = reader.ReadUInt32();
            r.MaxRxInviteRateRps = RpsFromUint16(reader.ReadUInt16());
            r.MaxRxRegisterRateRps = RpsFromUint16(reader.ReadUInt16());
            r.NeighborHMAC = HMAC.Decode(reader);
            
            // verify NeighborToken32
            if (!r.NeighborToken32.Equals(connectedPeerWhoSentTheRequest.LocalNeighborToken32))
                throw new BadSignatureException();

            // verify NeighborHMAC
            if (r.NeighborHMAC.Equals(
                connectedPeerWhoSentTheRequest.GetNeighborHMAC(r.GetSignedFieldsForNeighborHMAC)
                ) == false)
                throw new BadSignatureException();

            return r;
        }
    }
    public class PongPacket
    {
        /// <summary>
        /// authenticates sender peer at receiver side
        /// comes from ConnectionToNeighbor.RemoteNeighborToken32
        /// </summary>
        /// </summary>
        public NeighborToken32 NeighborToken32;
        public uint PingRequestId32;  // must match to request
       // byte Flags;
        const byte Flags_ResponderRegistrationConfirmationSignatureExists = 0x01;
        const byte FlagsMask_MustBeZero = 0b11110000;
        /// <summary>
        /// comes from responder neighbor when connection is set up; in other cases it is NULL
        /// signs fields: 
        /// { 
        ///    REQ shared fields,
        ///    ACK1 shared fields,
        ///    ACK2 shared fields,
        ///    ResponderRegistrationConfirmationSignature_MagicNumber
        /// } by responder's reg. private key
        /// is verified by EP, X to update rating of responder neighbor
        /// </summary>
        public RegistrationSignature ResponderRegistrationConfirmationSignature;

        public HMAC NeighborHMAC; // signs { NeighborToken32,PingRequestId32,(optional)ResponderRegistrationConfirmationSignature }

        /// <param name="reader">is positioned after first byte = packet type</param>
        public static PongPacket DecodeAndVerify(ICryptoLibrary cryptoLibrary,
            byte[] udpPayloadData, PingPacket optionalPingRequestPacketToCheckRequestId32, 
            ConnectionToNeighbor connectedPeerWhoSentTheResponse, bool requireSignature
            )
        {
            var reader = PacketProcedures.CreateBinaryReader(udpPayloadData, 1);
            var r = new PongPacket();
            r.NeighborToken32 = NeighborToken32.Decode(reader);
            r.PingRequestId32 = reader.ReadUInt32();
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
 
            // verify signature of N
            if ((flags & Flags_ResponderRegistrationConfirmationSignatureExists) != 0)
                r.ResponderRegistrationConfirmationSignature = RegistrationSignature.DecodeAndVerify(reader, cryptoLibrary, 
                    w => connectedPeerWhoSentTheResponse.GetResponderRegistrationConfirmationSignatureFields(w), 
                    connectedPeerWhoSentTheResponse.RemotePeerPublicKey);
            else
            {
                if (requireSignature) throw new UnmatchedFieldsException();
            }

            r.NeighborHMAC = HMAC.Decode(reader);

            if (optionalPingRequestPacketToCheckRequestId32 != null)
            {
                // verify PingRequestId32
                if (r.PingRequestId32 != optionalPingRequestPacketToCheckRequestId32.PingRequestId32)
                    throw new UnmatchedFieldsException();
            }

            // verify NeighborToken32
            if (!r.NeighborToken32.Equals(connectedPeerWhoSentTheResponse.LocalNeighborToken32))
                throw new UnmatchedFieldsException();

            // verify NeighborHMAC
            var expectedHMAC = connectedPeerWhoSentTheResponse.GetNeighborHMAC(r.GetSignedFieldsForNeighborHMAC);
            if (r.NeighborHMAC.Equals(expectedHMAC) == false)
            {
                connectedPeerWhoSentTheResponse.Engine.WriteToLog_p2p_detail(connectedPeerWhoSentTheResponse, $"incorrect sender HMAC in ping response: {r.NeighborHMAC}. expected: {expectedHMAC}");
                throw new BadSignatureException();
            }
          
            return r;
        }
        public static ushort DecodeToken16FromUdpPayloadData(byte[] udpPayloadData)
        { // first byte is packet type. then 4 bytes are NeighborToken32
            return (ushort)(udpPayloadData[1] | (udpPayloadData[2] << 8));
        }

        public static LowLevelUdpResponseScanner GetScanner(NeighborToken32 senderToken32, uint pingRequestId32)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            GetHeaderFields(w, senderToken32, pingRequestId32);
            return new LowLevelUdpResponseScanner { ResponseFirstBytes = ms.ToArray() };
        }
        static void GetHeaderFields(BinaryWriter writer, NeighborToken32 senderToken32, uint pingRequestId32)
        {
            writer.Write((byte)DrpPacketType.Pong);
            senderToken32.Encode(writer);
            writer.Write(pingRequestId32);
        }

        public void GetSignedFieldsForNeighborHMAC(BinaryWriter writer)
        {
            GetHeaderFields(writer, NeighborToken32, PingRequestId32);
            if (ResponderRegistrationConfirmationSignature != null)
                ResponderRegistrationConfirmationSignature.Encode(writer);
        }
                   
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            GetHeaderFields(writer, NeighborToken32, PingRequestId32);           
            byte flags = 0;
            if (ResponderRegistrationConfirmationSignature != null) flags |= Flags_ResponderRegistrationConfirmationSignatureExists;
            writer.Write(flags);
            if (ResponderRegistrationConfirmationSignature != null) ResponderRegistrationConfirmationSignature.Encode(writer);
            NeighborHMAC.Encode(writer);
            return ms.ToArray();
        }
    }
}
