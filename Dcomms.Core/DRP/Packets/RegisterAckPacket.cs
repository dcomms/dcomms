using Dcomms.Cryptography;
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
        public static byte Flag_ipv6 = 0x02;  // set if requester is accessible via ipv6 address. default (0) means ipv4
        public byte Flags;
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
        /// goes into N->A p2pStreamParameters
        /// </summary>
        public byte[] ToRequesterTxParametersEncrypted;

        HMAC SenderHMAC; // is NULL for A->RP
        public NextHopAckSequenceNumber16 NhaSeq16;

        public RegisterAckPacket()
        {
        }
        public static LowLevelUdpResponseScanner GetScanner(P2pStreamParameters p2pParams, RegistrationPublicKey requesterPublicKey_RequestID, uint registerSynTimestamp32S)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpPacketType.RegisterAckPacket);
          
            writer.Write((byte)0); // ignored flags

            if (p2pParams != null)
                p2pParams.RemotePeerToken32.Encode(writer);

            requesterPublicKey_RequestID.Encode(writer);
            writer.Write(registerSynTimestamp32S);

            return new LowLevelUdpResponseScanner
            {
                IgnoredByteAtOffset1 = 1,
                ResponseFirstBytes = ms.ToArray()
            };
        }
       
        /// <param name="txParametersToPeerNeighbor">is null for A->RP mode</param>
        public byte[] Encode(P2pStreamParameters txParametersToPeerNeighbor)
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
        /// <param name="txParameters">for direct p2p stream from N to A</param>
        public static RegisterAckPacket DecodeAndVerifyAtResponder(ICryptoLibrary cryptoLibrary, byte[] registerAckPacketData,
            byte[] localPrivateEcdhKey, RegisterSynPacket remoteRegisterSyn,
            RegisterSynAckPacket localRegisterSynAck,
            out P2pStreamParameters txParameters)
        {
            var reader = PacketProcedures.CreateBinaryReader(registerAckPacketData, 1);

            var r = new RegisterAckPacket();
            r.Flags = reader.ReadByte();
            if ((r.Flags & Flag_AtoRP) == 0) r.SenderToken32 = P2pConnectionToken32.Decode(reader);

            r.RequesterPublicKey_RequestID = RegistrationPublicKey.Decode(reader);
            r.RegisterSynTimestamp32S = reader.ReadUInt32();
            r.ToRequesterTxParametersEncrypted = reader.ReadBytes(16);
            txParameters = P2pStreamParameters.DecryptAtRegisterResponder(cryptoLibrary, localPrivateEcdhKey, remoteRegisterSyn, localRegisterSynAck, r);

            r.RequesterHMAC = HMAC.Decode(reader);
            if (r.RequesterHMAC.Equals(txParameters.GetSharedHmac(cryptoLibrary, w => r.GetCommonRequesterAndResponderFields(w, false, true))) == false)
                   throw new BadSignatureException();

            if ((r.Flags & Flag_AtoRP) == 0)
            {
                throw new NotImplementedException();
                //SenderHMAC = HMAC.Decode(reader);
            }

            r.NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);
                       
            return r;
        }
    }
}
