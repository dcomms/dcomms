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
            var nhaScanner = NextHopAckPacket.GetScanner(nhaSeq16);
            WriteToLog_reg_responderSide_detail($"waiting for nextHopAck, scanner: {MiscProcedures.ByteArrayToString(nhaScanner.ResponseFirstBytes)} nhaSeq={nhaSeq16}");
            var nextHopResponsePacketData = await SendUdpRequestAsync_Retransmit(
                     new PendingLowLevelUdpRequest(remoteEndpoint,
                         nhaScanner, 
                         DateTimeNowUtc, Configuration.UdpLowLevelRequests_ExpirationTimeoutS,
                         requestPacketData,                      
                         Configuration.UdpLowLevelRequests_InitialRetransmissionTimeoutS, Configuration.UdpLowLevelRequests_RetransmissionTimeoutIncrement
                     ));
            if (nextHopResponsePacketData == null)
                throw new DrpTimeoutException();

            var nextHopResponsePacket = new NextHopAckPacket(nextHopResponsePacketData);
            if (nextHopResponsePacket.StatusCode != NextHopResponseCode.accepted)
                throw new NextHopRejectedException(nextHopResponsePacket.StatusCode);
            return nextHopResponsePacket;
        }

        async Task<byte[]> SendUdpRequestAsync_Retransmit_WaitForResponse(byte[] requestPacketData, IPEndPoint remoteEndpoint, LowLevelUdpResponseScanner responseScanner)
        {
            var nextHopResponsePacketData = await SendUdpRequestAsync_Retransmit(
                     new PendingLowLevelUdpRequest(remoteEndpoint,
                         responseScanner, DateTimeNowUtc, Configuration.UdpLowLevelRequests_ExpirationTimeoutS,
                         requestPacketData,
                         Configuration.UdpLowLevelRequests_InitialRetransmissionTimeoutS, Configuration.UdpLowLevelRequests_RetransmissionTimeoutIncrement
                     ));
            if (nextHopResponsePacketData == null)
                throw new DrpTimeoutException();
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
        /// true if the response is linked to request, and the packet is processed
        /// </returns>
        bool PendingUdpRequests_ProcessPacket(IPEndPoint remoteEndpoint, byte[] udpPayloadData, DateTime receivedAtUtc)
        {
            for (var item = _pendingLowLevelUdpRequests.First; item != null; item = item.Next)
            {
                var request = item.Value;

            //    WriteToLog_receiver_detail($"matching to pending request... remoteEndpoint={remoteEndpoint}, udpPayloadData={MiscProcedures.ByteArrayToString(udpPayloadData)} " +
            //        $"request.RemoteEndpoint={request.RemoteEndpoint} request.ResponseScanner.ResponseFirstBytes={MiscProcedures.ByteArrayToString(request.ResponseScanner.ResponseFirstBytes)}");

                if (request.RemoteEndpoint.Equals(remoteEndpoint) && request.ResponseScanner.Scan(udpPayloadData))
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
        public LowLevelUdpResponseScanner ResponseScanner;
        public byte[] RequestPacketData; // is null when no need to retransmit request packet during the waiting
        public DateTime ExpirationTimeUTC;
        public DateTime? NextRetransmissionTimeUTC; // is null when no need to retransmit request packet during the waiting
        public TaskCompletionSource<byte[]> TaskCompletionSource = new TaskCompletionSource<byte[]>();
        public DateTime? ResponseReceivedAtUtc;
        double? _currentRetransmissionTimeoutS;
        public PendingLowLevelUdpRequest(IPEndPoint remoteEndpoint, LowLevelUdpResponseScanner responseScanner, DateTime timeUtc, 
            double expirationTimeoutS,
            byte[] requestPacketData = null, double? initialRetransmissionTimeoutS = null, double? retransmissionTimeoutIncrement = null)
        {
            _retransmissionTimeoutIncrement = retransmissionTimeoutIncrement;
            ExpirationTimeUTC = timeUtc.AddSeconds(expirationTimeoutS);
            _currentRetransmissionTimeoutS = initialRetransmissionTimeoutS;
            if (_currentRetransmissionTimeoutS.HasValue) NextRetransmissionTimeUTC = timeUtc.AddSeconds(_currentRetransmissionTimeoutS.Value);
            RemoteEndpoint = remoteEndpoint;
            ResponseScanner = responseScanner;
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
    public class LowLevelUdpResponseScanner
    {
        public byte[] ResponseFirstBytes;
        public int? IgnoredByteAtOffset1; // is set to position of 'flags' byte in the scanned response packet
        public bool Scan(byte[] udpPayloadData)
        {
            return MiscProcedures.EqualByteArrayHeader(ResponseFirstBytes, udpPayloadData, IgnoredByteAtOffset1);
        }
    }
}