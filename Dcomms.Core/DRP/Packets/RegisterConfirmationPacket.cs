using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{

    /// <summary>
    /// is sent by A when it receives signed ping response from N
    /// A->EP->M
    /// EP and X proxies have authorized the registration request already, by RequesterPublicKey_RequestID
    /// the proxies finalize the pending REGISTER request, erase state, 
    /// verify signatures of N and A, update RDRs and ratings
    /// </summary>
    public class RegisterConfirmationPacket
    {
        public byte[] OriginalUdpPayloadData;

        const byte Flag_AtoEP = 0x01;
        byte Flags;
        public bool AtoEP => (Flags & Flag_AtoEP) != 0;

        public P2pConnectionToken32 SenderToken32; // is not transmitted in A->EP request

        public RegistrationPublicKey RequesterPublicKey_RequestID;
        public uint RegisterSynTimestamp32S;
        /// <summary>
        /// comes from pingResponse packet from responder 
        /// is verified by N, EP,M  before updating rating
        /// </summary>
        public RegistrationSignature ResponderRegistrationConfirmationSignature;
        /// <summary>
        /// is verified by N, EP,M  before updating rating 
        /// includes RequesterSignature_MagicNumber
        /// </summary>
        public RegistrationSignature RequesterRegistrationConfirmationSignature;

        public HMAC SenderHMAC; // is NULL for A->EP
        public NextHopAckSequenceNumber16 NhaSeq16;

        const ushort RequesterRegistrationConfirmationSignature_MagicNumber = 0x39E1;
        public static void GetRequesterRegistrationConfirmationSignatureFields(BinaryWriter writer, RegistrationSignature responderRegistrationConfirmationSignature, 
            RegisterSynPacket syn, RegisterSynAckPacket synAck, RegisterAckPacket ack)
        {
            responderRegistrationConfirmationSignature.Encode(writer);
            syn.GetCommonRequesterProxierResponderFields(writer, true);
            synAck.GetCommonRequesterProxierResponderFields(writer, true, true);
            ack.GetCommonRequesterProxierResponderFields(writer, true, true);
            writer.Write(RequesterRegistrationConfirmationSignature_MagicNumber);

        }
        const ushort ResponderRegistrationConfirmationSignature_MagicNumber = 0x18F0;
        public static void GetResponderRegistrationConfirmationSignatureFields(BinaryWriter writer, RegisterSynPacket syn, RegisterSynAckPacket synAck, RegisterAckPacket ack)
        {
            syn.GetCommonRequesterProxierResponderFields(writer, true);
            synAck.GetCommonRequesterProxierResponderFields(writer, true, true);
            ack.GetCommonRequesterProxierResponderFields(writer, true, true);
            writer.Write(ResponderRegistrationConfirmationSignature_MagicNumber);
        }


        public static LowLevelUdpResponseScanner GetScanner(ConnectionToNeighbor connectionToNeighbor, RegistrationPublicKey requesterPublicKey_RequestID, uint registerSynTimestamp32S)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpPacketType.RegisterConfirmationPacket);
            
            w.Write((byte)0); // ignored flags

            if (connectionToNeighbor != null)
                connectionToNeighbor.LocalRxToken32.Encode(w);

            w.Write(registerSynTimestamp32S);        
            requesterPublicKey_RequestID.Encode(w);
            return new LowLevelUdpResponseScanner
            {
                IgnoredByteAtOffset1 = 1,
                ResponseFirstBytes = ms.ToArray(),
            };
        }

        /// <param name="connectionToNeighbor">is not null for packets between registered peers</param>
        public byte[] Encode(ConnectionToNeighbor connectionToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpPacketType.RegisterConfirmationPacket);
            Flags = 0;
            if (connectionToNeighbor == null) Flags |= Flag_AtoEP;
            writer.Write(Flags);
            if (connectionToNeighbor != null)
                connectionToNeighbor.RemotePeerToken32.Encode(writer);

            writer.Write(RegisterSynTimestamp32S);
            RequesterPublicKey_RequestID.Encode(writer);

            ResponderRegistrationConfirmationSignature.Encode(writer);
            RequesterRegistrationConfirmationSignature.Encode(writer);

            //   if (txParametersToPeerNeighbor != null)
            //       txParametersToPeerNeighbor.GetLocalSenderHmac(this).Encode(writer);

            NhaSeq16.Encode(writer);

            return ms.ToArray();
        }

        public static RegisterConfirmationPacket DecodeAndVerifyAtResponder(byte[] regCfmUdpPayload, RegisterSynPacket syn, ConnectionToNeighbor newConnectionToNeighbor)
        {
            var reader = PacketProcedures.CreateBinaryReader(regCfmUdpPayload, 1);

            var cfm = new RegisterConfirmationPacket();
            cfm.OriginalUdpPayloadData = regCfmUdpPayload;

            cfm.Flags = reader.ReadByte();
            if ((cfm.Flags & Flag_AtoEP) == 0) cfm.SenderToken32 = P2pConnectionToken32.Decode(reader);

            cfm.RegisterSynTimestamp32S = reader.ReadUInt32();
            cfm.AssertMatchToRegisterSyn(syn);
            cfm.RequesterPublicKey_RequestID = RegistrationPublicKey.Decode(reader);

            cfm.ResponderRegistrationConfirmationSignature = RegistrationSignature.DecodeAndVerify(reader, newConnectionToNeighbor.Engine.CryptoLibrary, 
                w => newConnectionToNeighbor.GetResponderRegistrationConfirmationSignatureFields(w),
                newConnectionToNeighbor.LocalDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPublicKey
                );
            cfm.RequesterRegistrationConfirmationSignature = RegistrationSignature.DecodeAndVerify(reader, newConnectionToNeighbor.Engine.CryptoLibrary,
                w => newConnectionToNeighbor.GetRequesterRegistrationConfirmationSignatureFields(w, cfm.ResponderRegistrationConfirmationSignature),
                newConnectionToNeighbor.RemotePeerPublicKey
                );
                                 
            if ((cfm.Flags & Flag_AtoEP) == 0)
            {
                throw new NotImplementedException();
                //SenderHMAC = HMAC.Decode(reader);
            }

            cfm.NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);

            return cfm;

        }
        void AssertMatchToRegisterSyn(RegisterSynPacket localRegisterSyn)
        {
            if (localRegisterSyn.RequesterPublicKey_RequestID.Equals(this.RequesterPublicKey_RequestID) == false)
                throw new UnmatchedFieldsException();
            if (localRegisterSyn.Timestamp32S != this.RegisterSynTimestamp32S)
                throw new UnmatchedFieldsException();
        }
    }

}
