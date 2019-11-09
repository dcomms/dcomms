using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    public class RoutedRequest
    {
        public readonly IPEndPoint ReceivedFromEndpoint;
        public readonly ConnectionToNeighbor ReceivedFromNeighborNullable; // is NULL in A-EP mode
        readonly DrpPeerEngine _engine;
        /// <summary>
        /// is NULL for request that is generated locally (when local peer sends INVITE/REGISTER)
        /// </summary>
        public readonly DateTime? ReqReceivedTimeUtc; 
        public bool CheckedRecentUniqueProxiedRequests;
        public readonly Logger Logger;
        public RoutedRequest(Logger logger, ConnectionToNeighbor receivedFromNeighborNullable, IPEndPoint receivedFromEndpoint,
            DateTime? reqReceivedTimeUtc, InviteRequestPacket inviteReqNullable, RegisterRequestPacket registerReqNullable)
        {
            InviteReq = inviteReqNullable;
            RegisterReq = registerReqNullable;
            if (InviteReq == null && RegisterReq == null) throw new ArgumentException();
            if (InviteReq != null && RegisterReq != null) throw new ArgumentException();

            ReceivedFromNeighborNullable = receivedFromNeighborNullable;
            ReceivedFromEndpoint = receivedFromEndpoint;
            Logger = logger;
            _engine = logger.Engine;
            ReqReceivedTimeUtc = reqReceivedTimeUtc;

            if (InviteReq != null) ReqP2pSeq16 = InviteReq.ReqP2pSeq16;
            else ReqP2pSeq16 = RegisterReq.ReqP2pSeq16;
        }

        public readonly InviteRequestPacket InviteReq;
        public readonly RegisterRequestPacket RegisterReq;
        public object Req => (object)InviteReq ?? RegisterReq;
        public RegistrationId RequesterRegistrationId => InviteReq?.RequesterRegistrationId ?? RegisterReq.RequesterRegistrationId;
        public readonly RequestP2pSequenceNumber16 ReqP2pSeq16;
        byte[] RequestUdpPayloadData => InviteReq?.DecodedUdpPayloadData ?? RegisterReq.DecodedUdpPayloadData;
        void GetSignedFieldsForNeighborHMAC(BinaryWriter w)
        {
            if (InviteReq != null) InviteReq.GetSignedFieldsForNeighborHMAC(w);
            else RegisterReq.GetSignedFieldsForNeighborHMAC(w);
        }

        bool _repliedWithNPA;
        public HashSet<ConnectionToNeighbor> TriedNeighbors = new HashSet<ConnectionToNeighbor>();
          
        /// <summary>
        /// sends FAILURE or NPACK
        /// </summary>
        public async Task SendErrorResponse(ResponseOrFailureCode responseCode)
        {
            Logger.WriteToLog_higherLevelDetail($">> SendErrorResponse(responseCode={responseCode}) ReqP2pSeq16={ReqP2pSeq16}");
            if (ReceivedFromNeighborNullable?.IsDisposed == true) return;
            if (_repliedWithNPA)
            {
                // send FAILURE
                await RespondWithFailure(responseCode);
            }
            else
            {
                // send NPACK
                SendNeighborPeerAck(responseCode);
            }
        }
        internal void SendNeighborPeerAck_accepted_IfNotAlreadyReplied()
        {
            ReceivedFromNeighborNullable?.AssertIsNotDisposed();
            if (!_repliedWithNPA)
                SendNeighborPeerAck(ResponseOrFailureCode.accepted);
        }
        internal void SendNeighborPeerAck(ResponseOrFailureCode responseCode)
        {
            var npAck = new NeighborPeerAckPacket
            {
                ReqP2pSeq16 = ReqP2pSeq16,
                ResponseCode = responseCode
            };

            if (ReceivedFromNeighborNullable != null)
            {
                npAck.NeighborToken32 = ReceivedFromNeighborNullable.RemoteNeighborToken32;
                npAck.NeighborHMAC = ReceivedFromNeighborNullable.GetNeighborHMAC(w => npAck.GetSignedFieldsForNeighborHMAC(w, GetSignedFieldsForNeighborHMAC));
            }

            var npAckUdpData = npAck.Encode(ReceivedFromNeighborNullable == null);
            _engine.RespondToRequestAndRetransmissions(RequestUdpPayloadData, npAckUdpData, ReceivedFromEndpoint);
            _repliedWithNPA = true;
        }

        //internal void SendErrorResponseToInviteReq(InviteRequestPacket req, IPEndPoint requesterEndpoint,
        //    ConnectionToNeighbor neighborWhoSentRequest, bool alreadyRepliedWithNPA, NextHopResponseOrFailureCode errorCode)
        //{
        //    Engine.WriteToLog_inv_proxySide_detail($"routing failed, executing SendErrorResponseToInviteReq()", req, this);
        //    if (alreadyRepliedWithNPA)
        //    {
        //        // send FAILURE
        //        _ = RespondToSourcePeerWithAck1_Error(requesterEndpoint, req, neighborWhoSentRequest, errorCode);
        //    }
        //    else
        //    {
        //        // send NPACK
        //        SendNeighborPeerAckResponseToReq(req, neighborWhoSentRequest, errorCode);
        //    }
        //}


        //internal void SendNeighborPeerAckResponseToRegisterReq(RegisterRequestPacket req, IPEndPoint requesterEndpoint, NextHopResponseOrFailureCode responseCode, ConnectionToNeighbor neighbor)
        //{
        //    neighbor?.AssertIsNotDisposed();
        //    var npAck = new NeighborPeerAckPacket
        //    {
        //        ReqP2pSeq16 = req.ReqP2pSeq16,
        //        ResponseCode = responseCode
        //    };
        //    if (neighbor != null)
        //    {
        //        npAck.NeighborToken32 = neighbor.RemoteNeighborToken32;
        //        npAck.NeighborHMAC = neighbor.GetNeighborHMAC(w => npAck.GetSignedFieldsForNeighborHMAC(w, req.GetSignedFieldsForNeighborHMAC));
        //    }
        //    var npAckUdpData = npAck.Encode(neighbor == null);

        //    RespondToRequestAndRetransmissions(req.DecodedUdpPayloadData, npAckUdpData, requesterEndpoint);

        //}


        public async Task RespondWithFailure(ResponseOrFailureCode responseCode)
        {
            ReceivedFromNeighborNullable?.AssertIsNotDisposed();

            var failure = new FailurePacket
            {
                ReqP2pSeq16 = ReqP2pSeq16,
                ResponseCode = responseCode,                
            };                   

            var failureUdpData = failure.Encode_OpionallySignNeighborHMAC(ReceivedFromNeighborNullable);
            await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck("RoutedRequest failure 123123",  failureUdpData, ReceivedFromEndpoint,
                        failure.ReqP2pSeq16, ReceivedFromNeighborNullable, failure.GetSignedFieldsForNeighborHMAC);

        }

    }
}
