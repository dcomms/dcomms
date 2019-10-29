using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP.Packets
{
    /// <summary>
    /// a part of REQ-NPACK -FAILURE-NPACK
    /// is sent to requester side in response to INVITE or REGISTER, in case of an error
    /// 
    /// the FAILURE is retransmitted until NPACK with same ReqP2pSeq16
    /// </summary>
    class FailurePacket
    {
        /// <summary>
        /// 1: if packet is transmitted between registering A and EP, 
        /// 0: if packet is transmitted between neighbor peers. NeighborHMAC is sent 
        /// </summary>
        static byte Flag_AtoEP = 0x01;
        const byte FlagsMask_MustBeZero = 0b11110000;
        public bool AtoEP => NeighborToken32 == null;

        /// <summary>
        /// is not transmitted in A-EP packet
        /// comes from ConnectionToNeighbor.RemoteNeighborToken32 in case when this packet goes over established P2P connection (flag A-EP is zero)
        /// </summary>
        public NeighborToken32 NeighborToken32;
        
        /// <summary>
        /// equals to NpaSeq of original INVITE/REGISTER request
        /// </summary>
        public RequestP2pSequenceNumber16 ReqP2pSeq16;


        public NextHopResponseOrFailureCode FailureCode;

        /// <summary>
        /// is NULL for A->EP packet
        /// uses common secret of neighbors within p2p connection
        /// </summary>
        public HMAC NeighborHMAC;

    }
}
