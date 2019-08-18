using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
	/// <summary>
    /// low-level requests, retransmissions  
    /// used for registration requester-side and for synAck responder-side
    /// </summary>
    public partial class DrpPeerEngine
    {
        /// <summary>
        /// accessed by engine thread only 
        /// todo optimize by having a dictionary based on sorted arrayHeader data
        /// </summary>
        LinkedList<PendingLowLevelUdpRequest> _pendingLowLevelUdpRequests = new LinkedList<PendingLowLevelUdpRequest>(); 

        async Task<NextHopAckPacket> SendUdpRequestAsync_Retransmit_WaitForNextHopAck(byte[] requestPacketData, IPEndPoint remoteEndpoint, NextHopAckSequenceNumber16 nhaSeq16)
        {
            PacketProcedures.CreateBinaryWriter(out var responseFirstBytesMS, out var responseFirstBytesW);
            NextHopAckPacket.EncodeHeader(responseFirstBytesW, nhaSeq16);
            var nextHopResponsePacketData = await SendUdpRequestAsync_Retransmit(
                     new PendingLowLevelUdpRequest(remoteEndpoint,
                         responseFirstBytesMS.ToArray(), DateTimeNowUtc, Configuration.UdpLowLevelRequests_ExpirationTimeoutS,
                         requestPacketData,                      
                         Configuration.UdpLowLevelRequests_InitialRetransmissionTimeoutS, Configuration.UdpLowLevelRequests_RetransmissionTimeoutIncrement
                     ));
            if (nextHopResponsePacketData == null)
                throw new NextHopTimeoutException();

            var nextHopResponsePacket = new NextHopAckPacket(nextHopResponsePacketData);
            if (nextHopResponsePacket.StatusCode != NextHopResponseCode.accepted)
                throw new NextHopRejectedException(nextHopResponsePacket.StatusCode);
            return nextHopResponsePacket;
        }

        async Task<byte[]> SendUdpRequestAsync_Retransmit_WaitForResponse(byte[] requestPacketData, IPEndPoint remoteEndpoint, byte[] responseFirstBytes)
        {
            var nextHopResponsePacketData = await SendUdpRequestAsync_Retransmit(
                     new PendingLowLevelUdpRequest(remoteEndpoint,
                         responseFirstBytes, DateTimeNowUtc, Configuration.UdpLowLevelRequests_ExpirationTimeoutS,
                         requestPacketData,
                         Configuration.UdpLowLevelRequests_InitialRetransmissionTimeoutS, Configuration.UdpLowLevelRequests_RetransmissionTimeoutIncrement
                     ));
            if (nextHopResponsePacketData == null)
                throw new NextHopTimeoutException();
            return nextHopResponsePacketData;
        }

        /// <summary>
        /// sends udp packet
        /// expects response from same IPEndpoint, with specified first bytes 
        /// retransmits the packet if no response
        /// returns null on timeout
        /// </summary>
        async Task<byte[]> SendUdpRequestAsync_Retransmit(PendingLowLevelUdpRequest request)
        {
            request.InitialTxTimeUTC = DateTimeNowUtc;
            SendPacket(request.RequestPacketData, request.RemoteEndpoint);          
            return await WaitForUdpResponseAsync(request);
        }
		internal void SendPacket(byte[] udpPayload, IPEndPoint remoteEndpoint)
        {
            _socket.Send(udpPayload, udpPayload.Length, remoteEndpoint);
        }
        async Task<byte[]> WaitForUdpResponseAsync(PendingLowLevelUdpRequest request)
        {
            _pendingLowLevelUdpRequests.AddLast(request);
            return await request.TaskCompletionSource.Task;
        }

        /// <summary>
        /// raises timeout events, retransmits packets
        /// </summary>
        void PendingUdpRequests_OnTimer100ms(DateTime timeNowUTC)
        {
            for (var item = _pendingLowLevelUdpRequests.First; item != null;)
            {
                var request = item.Value;
                if (timeNowUTC > request.ExpirationTimeUTC)
                {
                    var nextItem = item.Next;
                    _pendingLowLevelUdpRequests.Remove(item);
                    request.TaskCompletionSource.SetResult(null);
                    item = nextItem;
                    continue;
                }
                else if (timeNowUTC > request.NextRetransmissionTimeUTC)
                {
                    request.OnRetransmitted();
                    SendPacket(request.RequestPacketData, request.RemoteEndpoint);
                }
                item = item.Next;
            }
        }

        /// <summary>
        /// is executed by engine thread
        /// </summary>
        /// <returns>
        /// reue if the response is linked to request, and the packet is processed
        /// </returns>
        bool PendingUdpRequests_ProcessPacket(IPEndPoint remoteEndpoint, byte[] udpPayloadData, DateTime receivedAtUtc)
        {
            for (var item = _pendingLowLevelUdpRequests.First; item != null; item = item.Next)
            {
                var request = item.Value;
                if (request.RemoteEndpoint.Equals(remoteEndpoint) && MiscProcedures.EqualByteArrayHeader(request.ResponseFirstBytes, udpPayloadData))
                {
                    _pendingLowLevelUdpRequests.Remove(item);
                    request.ResponseReceivedAtUtc = receivedAtUtc;
                    request.TaskCompletionSource.SetResult(udpPayloadData);
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// waits for expirationTimeoutS timeout, or for response packet from remoteEndpoint with first bytes = responseFirstBytes
    /// optionally retransmits the request
    /// </summary>
    class PendingLowLevelUdpRequest
    {
        public IPEndPoint RemoteEndpoint;
        public byte[] ResponseFirstBytes;
        public byte[] RequestPacketData; // is null when no need to retransmit request packet during the waiting
        public DateTime ExpirationTimeUTC;
        public DateTime? NextRetransmissionTimeUTC; // is null when no need to retransmit request packet during the waiting
        public TaskCompletionSource<byte[]> TaskCompletionSource = new TaskCompletionSource<byte[]>();
        public DateTime? ResponseReceivedAtUtc;
        double? _currentRetransmissionTimeoutS;
        public PendingLowLevelUdpRequest(IPEndPoint remoteEndpoint, byte[] responseFirstBytes, DateTime timeUtc, 
            double expirationTimeoutS,
            byte[] requestPacketData = null, double? initialRetransmissionTimeoutS = null, double? retransmissionTimeoutIncrement = null)
        {
            _retransmissionTimeoutIncrement = retransmissionTimeoutIncrement;
            ExpirationTimeUTC = timeUtc.AddSeconds(expirationTimeoutS);
            _currentRetransmissionTimeoutS = initialRetransmissionTimeoutS;
            if (_currentRetransmissionTimeoutS.HasValue) NextRetransmissionTimeUTC = timeUtc.AddSeconds(_currentRetransmissionTimeoutS.Value);
            RemoteEndpoint = remoteEndpoint;
            ResponseFirstBytes = responseFirstBytes;
            RequestPacketData = requestPacketData;
        }
        public void OnRetransmitted()
        {
            if (NextRetransmissionTimeUTC == null) throw new InvalidOperationException();
            _currentRetransmissionTimeoutS *= _retransmissionTimeoutIncrement;
            NextRetransmissionTimeUTC = NextRetransmissionTimeUTC.Value.AddSeconds(_currentRetransmissionTimeoutS.Value);
        }
        readonly double? _retransmissionTimeoutIncrement;
        public DateTime? InitialTxTimeUTC;
    }
}