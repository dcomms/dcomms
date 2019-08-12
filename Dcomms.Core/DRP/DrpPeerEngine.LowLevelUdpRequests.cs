using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
	/// <summary>
    /// low-level requests, retransmissions   -- used for registration only
    /// </summary>
    public partial class DrpPeerEngine
    {

        class PendingLowLevelUdpRequest
        {
            public IPEndPoint RemoteEndpoint;
            public byte[] ResponseFirstBytes;
            public byte[] RequestPacketData; // is null when no need to retransmit request packet during the waiting
            public DateTime ExpirationTimeUTC;
            public DateTime NextRetransmissionTimeUTC;
            public TaskCompletionSource<byte[]> TaskCompletionSource = new TaskCompletionSource<byte[]>();
            public DateTime? ResponseReceivedAtUtc;
            TimeSpan CurrentRetransmissionTimeout;
            public PendingLowLevelUdpRequest(IPEndPoint remoteEndpoint, byte[] responseFirstBytes, byte[] requestPacketData, DateTime timeUtc,
                double expirationTimeoutS = 2, double initialRetransmissionTimeoutS = 0.2, double retransmissionTimeoutIncrement = 1.5)
            {
                _retransmissionTimeoutIncrement = retransmissionTimeoutIncrement;
                ExpirationTimeUTC = timeUtc.AddSeconds(expirationTimeoutS);
                CurrentRetransmissionTimeout = TimeSpan.FromSeconds(initialRetransmissionTimeoutS);
                NextRetransmissionTimeUTC = timeUtc + CurrentRetransmissionTimeout;
                RemoteEndpoint = remoteEndpoint;
                ResponseFirstBytes = responseFirstBytes;
                RequestPacketData = requestPacketData;
            }
            public void OnRetransmitted()
            {
                CurrentRetransmissionTimeout = TimeSpan.FromTicks((long)(CurrentRetransmissionTimeout.Ticks * _retransmissionTimeoutIncrement));
                NextRetransmissionTimeUTC += CurrentRetransmissionTimeout;
            }
            readonly double _retransmissionTimeoutIncrement;
            public DateTime? InitialTxTimeUTC;
        }
        LinkedList<PendingLowLevelUdpRequest> _pendingLowLevelUdpRequests = new LinkedList<PendingLowLevelUdpRequest>(); // accessed by engine thread only

        async Task<NextHopAckPacket> SendUdpRequestAsync_WaitForNextHopAck(byte[] requestPacketData, IPEndPoint remoteEndpoint, NextHopAckSequenceNumber16 nhaSeq16)
        {
            PacketProcedures.CreateBinaryWriter(out var responseFirstBytesMS, out var responseFirstBytesW);
            NextHopAckPacket.EncodeHeader(responseFirstBytesW, nhaSeq16);
            var nextHopResponsePacketData = await SendUdpRequestAsync(
                     new PendingLowLevelUdpRequest(remoteEndpoint,
                         responseFirstBytesMS.ToArray(),
                         requestPacketData,
                         DateTimeNowUtc
                     ));
            if (nextHopResponsePacketData == null)
                throw new NextHopTimeoutException();

            var nextHopResponsePacket = new NextHopAckPacket(PacketProcedures.CreateBinaryReader(nextHopResponsePacketData, 1));
            if (nextHopResponsePacket.StatusCode != NextHopResponseCode.accepted)
                throw new NextHopRejectedException(nextHopResponsePacket.StatusCode);
            return nextHopResponsePacket;
        }

        /// <summary>
        /// sends udp packet
        /// expects response from same IPEndpoint, with specified first bytes 
        /// retransmits the packet if no response
        /// returns null on timeout
        /// </summary>
        async Task<byte[]> SendUdpRequestAsync(PendingLowLevelUdpRequest request)
        {
            request.InitialTxTimeUTC = DateTimeNowUtc;
            _socket.Send(request.RequestPacketData, request.RequestPacketData.Length, request.RemoteEndpoint);
            return await WaitForUdpResponseAsync(request);
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
                    _socket.Send(request.RequestPacketData, request.RequestPacketData.Length, request.RemoteEndpoint);
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
}