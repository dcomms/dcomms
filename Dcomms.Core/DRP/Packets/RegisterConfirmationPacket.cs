﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{
    /// <summary>
    /// is sent by A when it receives signed ping response from N
    /// A->EP->M
    /// EP and X proxies have authorized the registration request already, by RequesterRegistrationId
    /// the proxies finalize the pending REGISTER request, erase state, 
    /// verify signatures of N and A, update RDRs and ratings
    /// </summary>
    public class RegisterConfirmationPacket
    {
        public byte[] DecodedUdpPayloadData;

        const byte Flag_AtoEP = 0x01;
        byte Flags;
        const byte FlagsMask_MustBeZero = 0b11110000;
        public bool AtoEP => (Flags & Flag_AtoEP) != 0;


        /// <summary>
        /// is not transmitted in A-EP packet
        /// comes from ConnectionToNeighbor.RemoteNeighborToken32 in case when this packet goes over established P2P connection (flag A-EP is zero)
        /// </summary>
        public NeighborToken32 NeighborToken32; 

        public RegistrationId RequesterRegistrationId;
        public uint ReqTimestamp32S;
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

        public NeighborPeerAckSequenceNumber16 NpaSeq16;
        public HMAC NeighborHMAC; // is NULL for A->EP

        const ushort RequesterRegistrationConfirmationSignature_MagicNumber = 0x39E1;
        public static void GetRequesterRegistrationConfirmationSignatureFields(BinaryWriter writer, RegistrationSignature responderRegistrationConfirmationSignature, 
            RegisterRequestPacket req, RegisterAck1Packet ack1, RegisterAck2Packet ack2)
        {
            responderRegistrationConfirmationSignature.Encode(writer);
            req.GetSharedSignedFields(writer, true);
            ack1.GetSharedSignedFields(writer, true, true);
            ack2.GetSharedSignedFields(writer, true, true);
            writer.Write(RequesterRegistrationConfirmationSignature_MagicNumber);

        }
        const ushort ResponderRegistrationConfirmationSignature_MagicNumber = 0x18F0;
        public static void GetResponderRegistrationConfirmationSignatureFields(BinaryWriter writer, RegisterRequestPacket req, RegisterAck1Packet ack1, RegisterAck2Packet ack2)
        {
            req.GetSharedSignedFields(writer, true);
            ack1.GetSharedSignedFields(writer, true, true);
            ack2.GetSharedSignedFields(writer, true, true);
            writer.Write(ResponderRegistrationConfirmationSignature_MagicNumber);
        }

        /// <param name="connectionToNeighborNullable">
        /// peer that sends CFM
        /// if not null - the scanner will verify CFM.NeighborHMAC
        /// </param>
        public static LowLevelUdpResponseScanner GetScanner(ConnectionToNeighbor connectionToNeighborNullable, RegistrationId requesterPublicKey_RequestID, uint registerSynTimestamp32S)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var w);
            w.Write((byte)DrpDmpPacketTypes.RegisterConfirmation);
            
            w.Write((byte)0); // ignored flags

            if (connectionToNeighborNullable != null)
                connectionToNeighborNullable.LocalNeighborToken32.Encode(w);

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
                    if (cfm.NeighborHMAC.Equals(connectionToNeighborNullable.GetNeighborHMAC(cfm.GetSignedFieldsForNeighborHMAC)) == false) return false;
                    return true;
                };
            }


            return r;
        }

        /// <param name="connectionToNeighbor">is not null for packets between registered peers</param>
        public byte[] Encode_OptionallySignNeighborHMAC(ConnectionToNeighbor connectionToNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpDmpPacketTypes.RegisterConfirmation);
            Flags = 0;
            if (connectionToNeighbor == null) Flags |= Flag_AtoEP;
            writer.Write(Flags);
            if (connectionToNeighbor != null)
            {
                NeighborToken32 = connectionToNeighbor.RemoteNeighborToken32;
                NeighborToken32.Encode(writer);
            }

            writer.Write(ReqTimestamp32S);
            RequesterRegistrationId.Encode(writer);

            ResponderRegistrationConfirmationSignature.Encode(writer);
            RequesterRegistrationConfirmationSignature.Encode(writer);

            NpaSeq16.Encode(writer);
            if (connectionToNeighbor != null)
            {
                this.NeighborHMAC = connectionToNeighbor.GetNeighborHMAC(this.GetSignedFieldsForNeighborHMAC);
                this.NeighborHMAC.Encode(writer);
            }


            return ms.ToArray();
        }

        public static RegisterConfirmationPacket DecodeAndOptionallyVerify(byte[] regCfmUdpPayload, RegisterRequestPacket synNullable, ConnectionToNeighbor newConnectionToRequesterAtResponderNullable)
        {
            var reader = PacketProcedures.CreateBinaryReader(regCfmUdpPayload, 1);

            var cfm = new RegisterConfirmationPacket();
            cfm.DecodedUdpPayloadData = regCfmUdpPayload;

            cfm.Flags = reader.ReadByte();
            if ((cfm.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            if ((cfm.Flags & Flag_AtoEP) == 0) cfm.NeighborToken32 = NeighborToken32.Decode(reader);

            cfm.ReqTimestamp32S = reader.ReadUInt32();
            cfm.RequesterRegistrationId = RegistrationId.Decode(reader);
            if (synNullable != null) cfm.AssertMatchToRegisterReq(synNullable);

            if (newConnectionToRequesterAtResponderNullable != null)
            {
                cfm.ResponderRegistrationConfirmationSignature = RegistrationSignature.DecodeAndVerify(reader, newConnectionToRequesterAtResponderNullable.Engine.CryptoLibrary,
                    w => newConnectionToRequesterAtResponderNullable.GetResponderRegistrationConfirmationSignatureFields(w),
                    newConnectionToRequesterAtResponderNullable.LocalDrpPeer.RegistrationConfiguration.LocalPeerRegistrationId
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
                
            cfm.NpaSeq16 = NeighborPeerAckSequenceNumber16.Decode(reader);                 
            if ((cfm.Flags & Flag_AtoEP) == 0)
            {
                cfm.NeighborHMAC = HMAC.Decode(reader);
            }

            return cfm;
        }
        void AssertMatchToRegisterReq(RegisterRequestPacket localRegisterReq)
        {
            if (localRegisterReq.RequesterRegistrationId.Equals(this.RequesterRegistrationId) == false)
                throw new UnmatchedFieldsException();
            if (localRegisterReq.ReqTimestamp32S != this.ReqTimestamp32S)
                throw new UnmatchedFieldsException();
        }
        
        internal void GetSignedFieldsForNeighborHMAC(BinaryWriter writer)
        {
            NeighborToken32.Encode(writer);
            writer.Write(ReqTimestamp32S);
            RequesterRegistrationId.Encode(writer);
            ResponderRegistrationConfirmationSignature.Encode(writer);
            RequesterRegistrationConfirmationSignature.Encode(writer);
            NpaSeq16.Encode(writer);
        }
    }

}
