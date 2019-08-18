using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{
    /// <summary>
    /// is sent from A to N with encrypted IP address of A
    /// A->RP->M->N
    /// peers remember the register request operation by RequesterPublicKey_RequestID 
    /// пиры уже авторизовали друг друга на этом этапе
    /// </summary>
    public class RegisterAckPacket
    {
        P2pConnectionToken32 SenderToken32;
        /// <summary>
        /// 1: if packet is transmitted from registering A to RP, 
        /// 0: if packet is transmitted between neighbor peers (from sender to receiver). SenderHMAC is sent 
        /// </summary>
        static byte Flag_AtoRP = 0x01;
        byte ReservedFlagsMustBeZero;
        public RegistrationPublicKey RequesterPublicKey_RequestID;
        public uint RegisterSynTimestamp32S;
        /// <summary>
        /// signs fields: {RequesterPublicKey_RequestID,RegisterSynTimestamp32S,ToRequesterTxParametersEncrypted }
        /// is verified by N (responder)
        /// </summary>
        public HMAC RequesterHMAC;
        /// <summary>
        /// IP address of A + UDP port + salt 
        /// initial IP address of A comes from RP 
        /// possible attacks by RP???
        /// 16 bytes
        /// </summary>
        public byte[] ToRequesterTxParametersEncrypted;

        HMAC SenderHMAC; // is NULL for A->RP
        public NextHopAckSequenceNumber16 NhaSeq16;

        public RegisterAckPacket()
        {
        }
        public static void EncodeHeader(BinaryWriter writer, EstablishedP2pStreamParameters p2pParams, RegistrationPublicKey requesterPublicKey_RequestID, uint registerSynTimestamp32S)
        {
            writer.Write((byte)DrpPacketType.RegisterAckPacket);
            byte flags = 0;
            if (p2pParams == null) flags |= Flag_AtoRP;
            writer.Write(flags);

            if (p2pParams != null)
                p2pParams.RemotePeerToken32.Encode(writer);

            requesterPublicKey_RequestID.Encode(writer);
            writer.Write(registerSynTimestamp32S);
        }
        public byte[] Encode(EstablishedP2pStreamParameters txParametersToPeerNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            
            writer.Write((byte)DrpPacketType.RegisterAckPacket);
            byte flags = 0;
            if (txParametersToPeerNeighbor == null) flags |= Flag_AtoRP;
            writer.Write(flags);

            if (txParametersToPeerNeighbor != null)
                txParametersToPeerNeighbor.RemotePeerToken32.Encode(writer);

            GetCommonRequesterAndResponderFields(writer, true, true);

            //   if (txParametersToPeerNeighbor != null)
            //       txParametersToPeerNeighbor.GetLocalSenderHmac(this).Encode(writer);
            NhaSeq16.Encode(writer);

            return ms.ToArray();
        }
        /// <summary>
        /// used for signature at requester; as source for p2p stream AEAD hash
        /// </summary>
        public void GetCommonRequesterAndResponderFields(BinaryWriter writer, bool includeRequesterHMAC, bool includeTxParameters)
        {
            RequesterPublicKey_RequestID.Encode(writer);
            writer.Write(RegisterSynTimestamp32S);
            if (includeTxParameters) writer.Write(ToRequesterTxParametersEncrypted);
            if (includeRequesterHMAC) RequesterHMAC.Encode(writer);
        }
        public RegisterAckPacket(byte[] registerAckPacketData)
        {
            var reader = PacketProcedures.CreateBinaryReader(registerAckPacketData, 1);

            var flags = reader.ReadByte();
            if ((flags & Flag_AtoRP) == 0) SenderToken32 = P2pConnectionToken32.Decode(reader);

            RequesterPublicKey_RequestID = RegistrationPublicKey.Decode(reader);
            RegisterSynTimestamp32S = reader.ReadUInt32();
            RequesterHMAC = HMAC.Decode(reader);
            //todo: verify, at responder

            if ((flags & Flag_AtoRP) == 0)
            {
                throw new NotImplementedException();
                //SenderHMAC = HMAC.Decode(reader);
            }

            NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);
        }
    }
}
