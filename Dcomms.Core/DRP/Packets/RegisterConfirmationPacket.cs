using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DRP.Packets
{

    /// <summary>
    /// is sent by A when it receives signed ping response from N
    /// A->RP->M
    /// RP and X proxies have authorized the registration request already, by RequesterPublicKey_RequestID
    /// the proxies finalize the pending REGISTER request, erase state, 
    /// verify signatures of N and A, update RDRs and ratings
    /// </summary>
    public class RegisterConfirmationPacket
    {
        const byte Flag_AtoRP = 0x01;
        public byte ReservedFlagsMustBeZero;

        public P2pConnectionToken32 SenderToken32; // is not transmitted in A->RP request

        public RegistrationPublicKey RequesterPublicKey_RequestID;
        public RegistrationSignature NeighborP2pConnectionSetupSignature; // comes from pingResponse packet from neighbor //  is verified by N, RP,M  before updating rating
        public RegistrationSignature RequesterSignature; // is verified by N, RP,M  before updating rating // includes RequesterSignature_MagicNumber

        public HMAC SenderHMAC; // is NULL for A->RP
        public NextHopAckSequenceNumber16 NhaSeq16;

        const ushort RequesterSignature_MagicNumber = 0x39E1;
        public void GetCommonFields(BinaryWriter writer, bool includeMagicNumber)
        {
            writer.Write(ReservedFlagsMustBeZero);
            RequesterPublicKey_RequestID.Encode(writer);
            NeighborP2pConnectionSetupSignature.Encode(writer);
            if (includeMagicNumber) writer.Write(RequesterSignature_MagicNumber);
        }

        /// <param name="txParametersToPeerNeighbor">is not null for packets between registered peers</param>
        public byte[] Encode(P2pStreamParameters txParametersToPeerNeighbor)
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);

            writer.Write((byte)DrpPacketType.RegisterConfirmationPacket);
            byte flags = 0;
            if (txParametersToPeerNeighbor != null) flags |= Flag_AtoRP;
            writer.Write(flags);
            if (txParametersToPeerNeighbor != null)
                txParametersToPeerNeighbor.RemotePeerToken32.Encode(writer);

            GetCommonFields(writer, false);

            //   if (txParametersToPeerNeighbor != null)
            //       txParametersToPeerNeighbor.GetLocalSenderHmac(this).Encode(writer);

            NhaSeq16.Encode(writer);

            return ms.ToArray();
        }
    }

}
