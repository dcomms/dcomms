using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP.Packets
{
    public class PingRequestPacket
    {
        public P2pConnectionToken32 SenderToken32;
        public byte ReservedFlagsMustBeZero;
        public ushort Timestamp; // msec
        public float? MaxRxInviteRateRps;   // signal from sender "how much I can receive via this p2p connection"
        public float? MaxRxRegisterRateRps; // signal from sender "how much I can receive via this p2p connection"
        public HMAC SenderHMAC;
    }
    public class PingResponsePacket
    {
        /// <summary>
        /// comes from responder neighbor when connection is set up; in other cases it is NULL
        /// signs fields: 
        /// { 
        ///    from syn packet:  RequesterPublicKey_RequestID,Timestamp32S,
        ///    from synAck packet: NeighborPublicKey,
        ///    magic const "0x7827"
        /// } by neighbor's reg. private key
        /// is verified by RP, X to update rating of responder neighbor
        /// </summary>
        public RegistrationSignature P2pConnectionSetupSignature;
        P2pConnectionToken32 SenderToken32;
        byte ReservedFlagsMustBeZero;
        ushort TimestampOfRequest; // msec
        HMAC SenderHMAC;
    }
}
