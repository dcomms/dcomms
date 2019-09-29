using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
	/// <summary>
    /// low-level requests, retransmissions  
    /// </summary>
    public partial class DrpPeerEngine
    {
        /// <summary>
        /// accessed by engine thread only 
        /// todo optimize by having a dictionary based on sorted arrayHeader data
        /// </summary>
        LinkedList<PendingLowLevelUdpRequest> _pendingLowLevelUdpRequests = new LinkedList<PendingLowLevelUdpRequest>(); 

        /// <param name="waitNhaFromNeighborNullable">is used to verify NPACK.NeighborHMAC</param>
        internal async Task<NeighborPeerAckPacket> OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(byte[] requestPacketDataNullable, IPEndPoint responderEndpoint, 
            NeighborPeerAckSequenceNumber16 npaSeq16, ConnectionToNeighbor waitNhaFromNeighborNullable = null, Action<BinaryWriter> npaRequestFieldsForNeighborHmacNullable = null)
        {
            var npaScanner = NeighborPeerAckPacket.GetScanner(npaSeq16, waitNhaFromNeighborNullable, npaRequestFieldsForNeighborHmacNullable);
            WriteToLog_udp_detail($"waiting for NPACK, scanner: {MiscProcedures.ByteArrayToString(npaScanner.ResponseFirstBytes)} nhaSeq={npaSeq16}");
            var nextHopResponsePacketData = await SendUdpRequestAsync_Retransmit(
                     new PendingLowLevelUdpRequest(responderEndpoint,
                         npaScanner, 
                         DateTimeNowUtc, Configuration.UdpLowLevelRequests_ExpirationTimeoutS,
                         requestPacketDataNullable,                      
                         Configuration.UdpLowLevelRequests_InitialRetransmissionTimeoutS, Configuration.UdpLowLevelRequests_RetransmissionTimeoutIncrement
                     ));
            if (nextHopResponsePacketData == null)
            {
                string msg = $"Did not get NPACK response to DRP request ";
                if (requestPacketDataNullable != null) msg += (DrpDmpPacketTypes)requestPacketDataNullable[0];
                msg += " - timeout expired";
                throw new DrpTimeoutException(msg);
            }

            var nextHopResponsePacket = new NeighborPeerAckPacket(nextHopResponsePacketData);
            if (nextHopResponsePacket.StatusCode != NextHopResponseCode.accepted)
            {
                if (nextHopResponsePacket.StatusCode == NextHopResponseCode.rejected_serviceUnavailable)
                    throw new NextHopRejectedExceptionServiceUnavailable();
                else throw new NextHopRejectedException(nextHopResponsePacket.StatusCode);
            }
            return nextHopResponsePacket;
        }

        internal async Task<byte[]> OptionallySendUdpRequestAsync_Retransmit_WaitForResponse(byte[] requestPacketDataNullable, IPEndPoint responderEndpoint, LowLevelUdpResponseScanner responseScanner, double? expirationTimeoutS = null)
        {
            var nextHopResponsePacketData = await SendUdpRequestAsync_Retransmit(
                     new PendingLowLevelUdpRequest(responderEndpoint,
                         responseScanner, DateTimeNowUtc, expirationTimeoutS ?? Configuration.UdpLowLevelRequests_ExpirationTimeoutS,
                         requestPacketDataNullable,
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
        internal async Task<byte[]> SendUdpRequestAsync_Retransmit(PendingLowLevelUdpRequest request)
        {
            request.InitialTxTimeUTC = DateTimeNowUtc;
            if (request.RequestPacketDataNullable != null) SendPacket(request.RequestPacketDataNullable, request.ResponderEndpoint);          
            return await WaitForUdpResponseAsync(request);
        }
		internal void SendPacket(byte[] udpPayload, IPEndPoint remoteEndpoint)
        {
            if (udpPayload.Length > 548)
                throw new ArgumentException("Transmitted UDP packet size is too big to bypass internet safely without fragmentation");
            _socket.Send(udpPayload, udpPayload.Length, remoteEndpoint);
            WriteToLog_udp_detail($"sent packet {(DrpDmpPacketTypes)udpPayload[0]} to {remoteEndpoint} ({udpPayload.Length} bytes)");
        }
        internal async Task<byte[]> WaitForUdpResponseAsync(PendingLowLevelUdpRequest request)
        {
            WriteToLog_udp_detail($"waiting for response to {request}");
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
                    
                    WriteToLog_udp_lightPain($"timer expired, removed pending request {request}");                   

                    request.TaskCompletionSource.SetResult(null);
                    item = nextItem;
                    continue;
                }
                else if (request.RequestPacketDataNullable != null && timeNowUTC > request.NextRetransmissionTimeUTC)
                {                 
                    WriteToLog_udp_lightPain($"retransmitting request {request}");
                    request.OnRetransmitted();
                    SendPacket(request.RequestPacketDataNullable, request.ResponderEndpoint);  
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
        bool PendingUdpRequests_ProcessPacket(IPEndPoint responderEndpoint, byte[] udpData, DateTime receivedAtUtc)
        {
            // todo optimize tis by storing pending requests indexed
            for (var item = _pendingLowLevelUdpRequests.First; item != null; item = item.Next)
            {
                var request = item.Value;

                WriteToLog_udp_deepDetail($"matching to pending request... responderEndpoint={responderEndpoint}, udpData={MiscProcedures.ByteArrayToString(udpData)} " +
                    $"request={request}" 
                   // + $" ResponseScanner.ResponseFirstBytes={MiscProcedures.ByteArrayToString(request.ResponseScanner.ResponseFirstBytes)}"
                    );

                try
                {
                    if (request.ResponderEndpoint.Equals(responderEndpoint) && request.ResponseScanner.Scan(udpData))
                    {
                        _pendingLowLevelUdpRequests.Remove(item);
                        request.ResponseReceivedAtUtc = receivedAtUtc;
                        request.TaskCompletionSource.SetResult(udpData);
                        return true;
                    }
                }
                catch (Exception exc)
                {
                    HandleExceptionInReceiverThread(exc);
                }
            }

          //  WriteToLog_udp_detail($"match to pending request was not found for packet from {responderEndpoint}, udpData={MiscProcedures.ByteArrayToString(udpData)}");

            return false;
        }

        /// <summary>
        /// accessed by engine thread only 
        /// todo optimize by having a dictionary based on sorted packet data
        /// </summary>
        LinkedList<ResponderToRetransmittedRequests> _respondersToRetransmittedRequests = new LinkedList<ResponderToRetransmittedRequests>();
        /// <summary>
        /// sends response to requests
        /// creates an object "ResponderToRetransmittedRequests" which handles further restransmitted requests (in case if the sent response gets lost)
        /// </summary>
        internal void RespondToRequestAndRetransmissions(byte[] requestUdpPayloadData, byte[] responseUdpPayloadData, IPEndPoint requesterEndpoint)
        {
            SendPacket(responseUdpPayloadData, requesterEndpoint);
            _respondersToRetransmittedRequests.AddLast(new ResponderToRetransmittedRequests
            {
                RequestUdpPayloadData = requestUdpPayloadData,
                ResponseUdpPayloadData = responseUdpPayloadData,
                RequesterEndpoint = requesterEndpoint,
                ExpirationTimeUTC = DateTimeNowUtc + Configuration.ResponderToRetransmittedRequestsTimeout
            });
        }


        bool RespondersToRetransmittedRequests_ProcessPacket(IPEndPoint requesterEndpoint, byte[] udpData)
        {
            for (var item = _respondersToRetransmittedRequests.First; item != null; item = item.Next)
            {
                var responder = item.Value;
                if (responder.RequesterEndpoint.Equals(requesterEndpoint) && MiscProcedures.EqualByteArrays(responder.RequestUdpPayloadData, udpData))
                {
                    SendPacket(responder.ResponseUdpPayloadData, requesterEndpoint);
                    return true;
                }
            }
            return false;
        }

        void RespondersToRetransmittedRequests_OnTimer100ms(DateTime timeNowUTC)
        {
            for (var item = _respondersToRetransmittedRequests.First; item != null;)
            {
                var request = item.Value;
                if (timeNowUTC > request.ExpirationTimeUTC)
                {
                    var nextItem = item.Next;
                    _respondersToRetransmittedRequests.Remove(item);
                    item = nextItem;
                    continue;
                }                
                item = item.Next;
            }
        }
    }

    /// <summary>
    /// waits for expirationTimeoutS timeout, or for response packet from responderEndpoint with first bytes = responseFirstBytes
    /// optionally retransmits the request
    /// </summary>
    class PendingLowLevelUdpRequest
    {
        public IPEndPoint ResponderEndpoint;
        public LowLevelUdpResponseScanner ResponseScanner;
        public byte[] RequestPacketDataNullable; // is null when no need to retransmit request packet during the waiting
        public DateTime ExpirationTimeUTC;
        public DateTime? NextRetransmissionTimeUTC; // is null when no need to retransmit request packet during the waiting
        public TaskCompletionSource<byte[]> TaskCompletionSource = new TaskCompletionSource<byte[]>();
        public DateTime? ResponseReceivedAtUtc;
        double? _currentRetransmissionTimeoutS;
        readonly double _expirationTimeoutS;
        public PendingLowLevelUdpRequest(IPEndPoint responderEndpoint, LowLevelUdpResponseScanner responseScanner, DateTime timeUtc,
            double expirationTimeoutS,
            byte[] requestPacketDataNullable = null, double? initialRetransmissionTimeoutS = null, double? retransmissionTimeoutIncrement = null)
        {
            _retransmissionTimeoutIncrement = retransmissionTimeoutIncrement;
            ExpirationTimeUTC = timeUtc.AddSeconds(expirationTimeoutS);
            _currentRetransmissionTimeoutS = initialRetransmissionTimeoutS;
            if (_currentRetransmissionTimeoutS.HasValue) NextRetransmissionTimeUTC = timeUtc.AddSeconds(_currentRetransmissionTimeoutS.Value);
            ResponderEndpoint = responderEndpoint;
            ResponseScanner = responseScanner;
            RequestPacketDataNullable = requestPacketDataNullable;
            _expirationTimeoutS = expirationTimeoutS;
        }
        public override string ToString()
        {
            var r = $"responderEP={ResponderEndpoint}";
            if (RequestPacketDataNullable != null)
                r += $", req={(DrpDmpPacketTypes)RequestPacketDataNullable[0]}";
            if (ResponseScanner != null && ResponseScanner.ResponseFirstBytes != null)
                r += $", resp={(DrpDmpPacketTypes)ResponseScanner.ResponseFirstBytes[0]}";
            r += $", timeout={_expirationTimeoutS}s";
            return r;
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
        public Func<byte[],bool> OptionalFilter; // verifies NPACK.NeighborHMAC, ignores invalid HMACs // returns false to ignore the processed response packet

        public bool Scan(byte[] udpData) // may throw parser exception
        {
            if (!MiscProcedures.EqualByteArrayHeader(ResponseFirstBytes, udpData, IgnoredByteAtOffset1)) return false;
            if (OptionalFilter != null)
            {
                if (!OptionalFilter(udpData)) return false;               
            }
            return true;
        }
    }

    class ResponderToRetransmittedRequests
    {
        public byte[] RequestUdpPayloadData;
        public byte[] ResponseUdpPayloadData;
        public IPEndPoint RequesterEndpoint;
        public DateTime ExpirationTimeUTC;
    }
}