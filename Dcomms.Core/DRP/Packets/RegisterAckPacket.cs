using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{
    /// <summary>
    /// is sent from A to N with encrypted IP address of A
    /// A->EP->M->N
    /// peers remember the register request operation by RequesterPublicKey_RequestID 
    /// пиры уже авторизовали друг друга на этом этапе
    /// </summary>
    public class RegisterAckPacket
    {
        /// <summary>
        /// is not transmitted in A-EP packet
        /// comes from ConnectionToNeighbor.RemotePeerToken32 in case when this packet goes ofver established P2P connection (flag A-EP is zero)
        /// </summary>
        public P2pConnectionToken32 SenderToken32;
        /// <summary>
        /// 1: if packet is transmitted from registering A to EP, 
        /// 0: if packet is transmitted between neighbor peers (from sender to receiver). SenderHMAC is sent 
        /// </summary>
        static byte Flag_AtoEP = 0x01;
        public static byte Flag_ipv6 = 0x02;  // set if requester is accessible via ipv6 address. default (0) means ipv4
        public byte Flags;
        public bool AtoEP => (Flags & Flag_AtoEP) != 0;
        const byte FlagsMask_MustBeZero = 0b11110000;
        public uint RegisterSynTimestamp32S;
        public RegistrationPublicKey RequesterPublicKey_RequestID;
        /// <summary>
        /// IP address of A + UDP port + salt 
        /// initial IP address of A comes from EP 
        /// possible attacks by EP???
        /// 16 bytes
        /// goes into N->A p2pStreamParameters
        /// </summary>
        public byte[] ToRequesterTxParametersEncrypted;
        /// <summary>
        /// signs fields: {RequesterPublicKey_RequestID,RegisterSynTimestamp32S,ToRequesterTxParametersEncrypted }
        /// is verified by N (responder)
        /// </summary>
        public HMAC RequesterHMAC;

        public HMAC SenderHMAC; // is NULL for A->EP
        public NextHopAckSequenceNumber16 NhaSeq16;
        public byte[] OriginalUdpPayloadData;
        public RegisterAckPacket()
        {
        }
        /// <param name="connectionToNeighborNullable">
        /// peer that sends ACK
        /// if not null - the scanner will verify ACK.SenderHMAC
        /// </param>
        public static LowLevelUdpResponseScanner GetScanner(ConnectionToNeighbor connectionToNeighborNullable, RegistrationPublicKey requesterPublicKey_RequestID, uint registerSynTimestamp32S)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpPacketType.RegisterAck);
          
            writer.Write((byte)0); // ignored flags

            if (connectionToNeighborNullable != null)
            {
                connectionToNeighborNullable.LocalRxToken32.Encode(writer);
            }

            writer.Write(registerSynTimestamp32S);
            requesterPublicKey_RequestID.Encode(writer);

            var r = new LowLevelUdpResponseScanner
            {
                IgnoredByteAtOffset1 = 1,
                ResponseFirstBytes = ms.ToArray()
            };
            if (connectionToNeighborNullable != null)
            {
                r.OptionalFilter = (responseData) =>
                {
                    var ack = Decode_OptionallyVerify_InitializeP2pStreamAtResponder(responseData, null, null, null);
                    if (ack.SenderHMAC.Equals(connectionToNeighborNullable.GetSenderHMAC(ack.GetSignedFieldsForSenderHMAC)) == false) return false;
                    return true;
                };
            }


            return r;
        }

        /// <param name="connectionToNeighbor">is null for A->EP mode</param>
        public byte[] Encode_OptionallySignSenderHMAC(ConnectionToNeighbor connectionToNeighborNullable)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            
            writer.Write((byte)DrpPacketType.RegisterAck);
            byte flags = 0;
            if (connectionToNeighborNullable == null) flags |= Flag_AtoEP;
            writer.Write(flags);

            if (connectionToNeighborNullable != null)
            {
                SenderToken32 = connectionToNeighborNullable.RemotePeerToken32;
                SenderToken32.Encode(writer);
            }

            GetCommonRequesterProxyResponderFields(writer, true, true);

            NhaSeq16.Encode(writer);

            if (connectionToNeighborNullable != null)
                connectionToNeighborNullable.GetSenderHMAC(this.GetSignedFieldsForSenderHMAC).Encode(writer);
            
            return ms.ToArray();
        }
        /// <summary>
        /// used for signature at requester; as source for p2p stream AEAD hash
        /// </summary>
        public void GetCommonRequesterProxyResponderFields(BinaryWriter writer, bool includeRequesterHMAC, bool includeTxParameters)
        {
            writer.Write(RegisterSynTimestamp32S);
            RequesterPublicKey_RequestID.Encode(writer);
            if (includeTxParameters) writer.Write(ToRequesterTxParametersEncrypted);
            if (includeRequesterHMAC) RequesterHMAC.Encode(writer);
        }
        
        internal void GetSignedFieldsForSenderHMAC(BinaryWriter writer)
        {
            SenderToken32.Encode(writer);
            GetCommonRequesterProxyResponderFields(writer, true, true);
            NhaSeq16.Encode(writer);
        }

        void AssertMatchToSyn(RegisterSynPacket remoteRegisterSyn)
        {
            if (!this.RequesterPublicKey_RequestID.Equals(remoteRegisterSyn.RequesterPublicKey_RequestID))
                throw new UnmatchedFieldsException();
            if (this.RegisterSynTimestamp32S != remoteRegisterSyn.Timestamp32S)
                throw new UnmatchedFieldsException();
        }

        /// <param name="newConnectionAtResponderToRequesterNullable">
        /// direct P2P stream from N to A
        /// if newConnectionAtResponderToRequesterNullable is specified, the procedure 
        /// verifies RequesterHMAC, decrypts endpoint of A (ToRequesterTxParametersEncrypted), initializes P2P stream
        /// </param>
        public static RegisterAckPacket Decode_OptionallyVerify_InitializeP2pStreamAtResponder(byte[] registerAckPacketData,
            RegisterSynPacket synNullable,
            RegisterSynAckPacket synAckNullable,
            ConnectionToNeighbor newConnectionAtResponderToRequesterNullable
          )
        {
            var reader = PacketProcedures.CreateBinaryReader(registerAckPacketData, 1);

            var ack = new RegisterAckPacket();
            ack.OriginalUdpPayloadData = registerAckPacketData;
            ack.Flags = reader.ReadByte();
            if ((ack.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            if ((ack.Flags & Flag_AtoEP) == 0) ack.SenderToken32 = P2pConnectionToken32.Decode(reader);

            ack.RegisterSynTimestamp32S = reader.ReadUInt32();
            ack.RequesterPublicKey_RequestID = RegistrationPublicKey.Decode(reader);
            if (synNullable != null) ack.AssertMatchToSyn(synNullable);

            ack.ToRequesterTxParametersEncrypted = reader.ReadBytes(16);
            if (newConnectionAtResponderToRequesterNullable != null)
            {
                newConnectionAtResponderToRequesterNullable.Decrypt_ack_ToRequesterTxParametersEncrypted_AtResponder_InitializeP2pStream(synNullable, synAckNullable, ack);
            }

            ack.RequesterHMAC = HMAC.Decode(reader);
            if (newConnectionAtResponderToRequesterNullable != null)
            {
                if (ack.RequesterHMAC.Equals(newConnectionAtResponderToRequesterNullable.GetSenderHMAC(w => ack.GetCommonRequesterProxyResponderFields(w, false, true))) == false)
                    throw new BadSignatureException();
            }

            ack.NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);

            if ((ack.Flags & Flag_AtoEP) == 0)
            {
                ack.SenderHMAC = HMAC.Decode(reader); // is verified by Filter
            }
                       
            return ack;
        }
    }
}
