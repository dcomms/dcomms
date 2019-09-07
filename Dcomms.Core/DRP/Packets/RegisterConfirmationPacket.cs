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
        const byte FlagsMask_MustBeZero = 0b11000000;
        public bool AtoEP => (Flags & Flag_AtoEP) != 0;


        /// <summary>
        /// is not transmitted in A-EP packet
        /// comes from ConnectionToNeighbor.RemotePeerToken32 in case when this packet goes over established P2P connection (flag A-EP is zero)
        /// </summary>
        public P2pConnectionToken32 SenderToken32; 

        public RegistrationPublicKey RequesterPublicKey_RequestID;
        public uint RegisterSynTimestamp32S;
        /// <summary>
        /// comes from pong packet from responder 
        /// is verified by N, EP,M  before updating rating
        /// </summary>
        public RegistrationSignature ResponderRegistrationConfirmationSignature;
        /// <summary>
        /// is verified by N, EP,M  before updating rating 
        /// includes RequesterSignature_MagicNumber
        /// </summary>
        public RegistrationSignature RequesterRegistrationConfirmationSignature;

        public NextHopAckSequenceNumber16 NhaSeq16;
        public HMAC SenderHMAC; // is NULL for A->EP

        const ushort RequesterRegistrationConfirmationSignature_MagicNumber = 0x39E1;
        public static void GetRequesterRegistrationConfirmationSignatureFields(BinaryWriter writer, RegistrationSignature responderRegistrationConfirmationSignature, 
            RegisterSynPacket syn, RegisterSynAckPacket synAck, RegisterAckPacket ack)
        {
            responderRegistrationConfirmationSignature.Encode(writer);
            syn.GetCommonRequesterProxyResponderFields(writer, true);
            synAck.GetCommonRequesterProxierResponderFields(writer, true, true);
            ack.GetCommonRequesterProxyResponderFields(writer, true, true);
            writer.Write(RequesterRegistrationConfirmationSignature_MagicNumber);

        }
        const ushort ResponderRegistrationConfirmationSignature_MagicNumber = 0x18F0;
        public static void GetResponderRegistrationConfirmationSignatureFields(BinaryWriter writer, RegisterSynPacket syn, RegisterSynAckPacket synAck, RegisterAckPacket ack)
        {
            syn.GetCommonRequesterProxyResponderFields(writer, true);
            synAck.GetCommonRequesterProxierResponderFields(writer, true, true);
            ack.GetCommonRequesterProxyResponderFields(writer, true, true);
            writer.Write(ResponderRegistrationConfirmationSignature_MagicNumber);
        }

        /// <param name="connectionToNeighborNullable">
        /// peer that sends CFM
        /// if not null - the scanner will verify CFM.SenderHMAC
        /// </param>
        public static LowLevelUdpResponseScanner GetScanner(ConnectionToNeighbor connectionToNeighborNullable, RegistrationPublicKey requesterPublicKey_RequestID, uint registerSynTimestamp32S)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpPacketType.RegisterConfirmation);
            
            w.Write((byte)0); // ignored flags

            if (connectionToNeighborNullable != null)
                connectionToNeighborNullable.LocalRxToken32.Encode(w);

            w.Write(registerSynTimestamp32S);        
            requesterPublicKey_RequestID.Encode(w);
            var r = new LowLevelUdpResponseScanner
            {
                IgnoredByteAtOffset1 = 1,
                ResponseFirstBytes = ms.ToArray(),
            };

            if (connectionToNeighborNullable != null)
            {
                r.OptionalFilter = (responseData) =>
                {
                    var cfm = DecodeAndOptionallyVerify(responseData, null, null);
                    if (cfm.SenderHMAC.Equals(connectionToNeighborNullable.GetSenderHMAC(cfm.GetSignedFieldsForSenderHMAC)) == false) return false;
                    return true;
                };
            }


            return r;
        }

        /// <param name="connectionToNeighbor">is not null for packets between registered peers</param>
        public byte[] Encode_OptionallySignSenderHMAC(ConnectionToNeighbor connectionToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpPacketType.RegisterConfirmation);
            Flags = 0;
            if (connectionToNeighbor == null) Flags |= Flag_AtoEP;
            writer.Write(Flags);
            if (connectionToNeighbor != null)
            {
                SenderToken32 = connectionToNeighbor.RemotePeerToken32;
                SenderToken32.Encode(writer);
            }

            writer.Write(RegisterSynTimestamp32S);
            RequesterPublicKey_RequestID.Encode(writer);

            ResponderRegistrationConfirmationSignature.Encode(writer);
            RequesterRegistrationConfirmationSignature.Encode(writer);

            NhaSeq16.Encode(writer);
            if (connectionToNeighbor != null)
            {
                this.SenderHMAC = connectionToNeighbor.GetSenderHMAC(this.GetSignedFieldsForSenderHMAC);
                this.SenderHMAC.Encode(writer);
            }


            return ms.ToArray();
        }

        public static RegisterConfirmationPacket DecodeAndOptionallyVerify(byte[] regCfmUdpPayload, RegisterSynPacket synNullable, ConnectionToNeighbor newConnectionToRequesterAtResponderNullable)
        {
            var reader = PacketProcedures.CreateBinaryReader(regCfmUdpPayload, 1);

            var cfm = new RegisterConfirmationPacket();
            cfm.OriginalUdpPayloadData = regCfmUdpPayload;

            cfm.Flags = reader.ReadByte();
            if ((cfm.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            if ((cfm.Flags & Flag_AtoEP) == 0) cfm.SenderToken32 = P2pConnectionToken32.Decode(reader);

            cfm.RegisterSynTimestamp32S = reader.ReadUInt32();
            cfm.RequesterPublicKey_RequestID = RegistrationPublicKey.Decode(reader);
            if (synNullable != null) cfm.AssertMatchToRegisterSyn(synNullable);

            if (newConnectionToRequesterAtResponderNullable != null)
            {
                cfm.ResponderRegistrationConfirmationSignature = RegistrationSignature.DecodeAndVerify(reader, newConnectionToRequesterAtResponderNullable.Engine.CryptoLibrary,
                    w => newConnectionToRequesterAtResponderNullable.GetResponderRegistrationConfirmationSignatureFields(w),
                    newConnectionToRequesterAtResponderNullable.LocalDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPublicKey
                    );
                cfm.RequesterRegistrationConfirmationSignature = RegistrationSignature.DecodeAndVerify(reader, newConnectionToRequesterAtResponderNullable.Engine.CryptoLibrary,
                    w => newConnectionToRequesterAtResponderNullable.GetRequesterRegistrationConfirmationSignatureFields(w, cfm.ResponderRegistrationConfirmationSignature),
                    newConnectionToRequesterAtResponderNullable.RemotePeerPublicKey
                    );
            }
            else
            {
                cfm.ResponderRegistrationConfirmationSignature = RegistrationSignature.Decode(reader);
                cfm.RequesterRegistrationConfirmationSignature = RegistrationSignature.Decode(reader);
            }
                
            cfm.NhaSeq16 = NextHopAckSequenceNumber16.Decode(reader);                 
            if ((cfm.Flags & Flag_AtoEP) == 0)
            {
                cfm.SenderHMAC = HMAC.Decode(reader);
            }

            return cfm;
        }
        void AssertMatchToRegisterSyn(RegisterSynPacket localRegisterSyn)
        {
            if (localRegisterSyn.RequesterPublicKey_RequestID.Equals(this.RequesterPublicKey_RequestID) == false)
                throw new UnmatchedFieldsException();
            if (localRegisterSyn.Timestamp32S != this.RegisterSynTimestamp32S)
                throw new UnmatchedFieldsException();
        }
        
        internal void GetSignedFieldsForSenderHMAC(BinaryWriter writer)
        {
            SenderToken32.Encode(writer);
            writer.Write(RegisterSynTimestamp32S);
            RequesterPublicKey_RequestID.Encode(writer);
            ResponderRegistrationConfirmationSignature.Encode(writer);
            RequesterRegistrationConfirmationSignature.Encode(writer);
            NhaSeq16.Encode(writer);
        }
    }

}
