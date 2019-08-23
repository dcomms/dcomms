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
        /// IP address of A + UDP port + salt 
        /// initial IP address of A comes from RP 
        /// possible attacks by RP???
        /// 16 bytes
        /// goes into N->A p2pStreamParameters
        /// </summary>
        public byte[] ToRequesterTxParametersEncrypted;
        /// <summary>
        /// signs fields: {RequesterPublicKey_RequestID,RegisterSynTimestamp32S,ToRequesterTxParametersEncrypted }
        /// is verified by N (responder)
        /// </summary>
        public HMAC RequesterHMAC;

        HMAC SenderHMAC; // is NULL for A->RP
        public NextHopAckSequenceNumber16 NhaSeq16;

        public RegisterAckPacket()
        {
        }
        public static LowLevelUdpResponseScanner GetScanner(ConnectionToNeighbor connectionToNeighbor, RegistrationPublicKey requesterPublicKey_RequestID, uint registerSynTimestamp32S)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpPacketType.RegisterAckPacket);
          
            writer.Write((byte)0); // ignored flags

            if (connectionToNeighbor != null)
                connectionToNeighbor.LocalRxToken32.Encode(writer);

            requesterPublicKey_RequestID.Encode(writer);
            writer.Write(registerSynTimestamp32S);

            return new LowLevelUdpResponseScanner
            {
                IgnoredByteAtOffset1 = 1,
                ResponseFirstBytes = ms.ToArray()
            };
        }

        /// <param name="connectionToNeighbor">is null for A->RP mode</param>
        public byte[] Encode(ConnectionToNeighbor connectionToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            
            writer.Write((byte)DrpPacketType.RegisterAckPacket);
            byte flags = 0;
            if (connectionToNeighbor == null) flags |= Flag_AtoRP;
            writer.Write(flags);

            if (connectionToNeighbor != null)
                connectionToNeighbor.RemotePeerToken32.Encode(writer);

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
        void AssertMatchToSyn(RegisterSynPacket remoteRegisterSyn)
        {
            if (!this.RequesterPublicKey_RequestID.Equals(remoteRegisterSyn.RequesterPublicKey_RequestID))
                throw new UnmatchedFieldsException();
            if (this.RegisterSynTimestamp32S != remoteRegisterSyn.Timestamp32S)
                throw new UnmatchedFieldsException();
        }
        /// <param name="connectionFromResponderToRequester">for direct p2p stream from N to A</param>
        public static RegisterAckPacket DecodeAndVerifyAtResponder(byte[] registerAckPacketData,
            RegisterSynPacket remoteRegisterSyn,
            RegisterSynAckPacket localRegisterSynAck,
            ConnectionToNeighbor connectionFromResponderToRequester
          )
        {
            var reader = PacketProcedures.CreateBinaryReader(registerAckPacketData, 1);

            var registerAck = new RegisterAckPacket();
            registerAck.Flags = reader.ReadByte();
            if ((registerAck.Flags & Flag_AtoRP) == 0) registerAck.SenderToken32 = P2pConnectionToken32.Decode(reader);

            registerAck.RequesterPublicKey_RequestID = RegistrationPublicKey.Decode(reader);
            registerAck.RegisterSynTimestamp32S = reader.ReadUInt32();
            registerAck.ToRequesterTxParametersEncrypted = reader.ReadBytes(16);
            registerAck.AssertMatchToSyn(remoteRegisterSyn);
            connectionFromResponderToRequester.DecryptAtRegisterResponder(remoteRegisterSyn, localRegisterSynAck, registerAck);

            registerAck.RequesterHMAC = HMAC.Decode(reader);
            if (registerAck.RequesterHMAC.Equals(connectionFromResponderToRequester.GetSharedHmac(w => registerAck.GetCommonRequesterAndResponderFields(w, false, true))) == false)
                   throw new BadSignatureException();

            if ((registerAck.Flags & Flag_AtoRP) == 0)
            {
                throw new NotImplementedException();
                //SenderHMAC = HMAC.Decode(reader);
            }

            registerAck.NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);
                       
            return registerAck;
        }
    }
}
