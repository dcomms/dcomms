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
            RequestP2pSequenceNumber16 reqP2pSeq16, ConnectionToNeighbor waitNhaFromNeighborNullable = null, Action<BinaryWriter> npaRequestFieldsForNeighborHmacNullable = null)
        {
            var npaScanner = NeighborPeerAckPacket.GetScanner(reqP2pSeq16, waitNhaFromNeighborNullable, npaRequestFieldsForNeighborHmacNullable);
            if (WriteToLog_udp_deepDetail_enabled) WriteToLog_udp_deepDetail($"waiting for NPACK, scanner: {MiscProcedures.ByteArrayToString(npaScanner.ResponseFirstBytes)} npaSeq={reqP2pSeq16}");
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
                if (requestPacketDataNullable != null) msg += (PacketTypes)requestPacketDataNullable[0];
                msg += " - timeout expired";
                throw new DrpTimeoutException(msg);
            }

            var nextHopResponsePacket = new NeighborPeerAckPacket(nextHopResponsePacketData);
            if (nextHopResponsePacket.ResponseCode != ResponseOrFailureCode.accepted)
            {
                throw new RequestRejectedException(nextHopResponsePacket.ResponseCode);
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
            if (WriteToLog_udp_deepDetail_enabled) WriteToLog_udp_deepDetail($"sending packet {(PacketTypes)udpPayload[0]} to {remoteEndpoint} ({udpPayload.Length} bytes, hash={MiscProcedures.GetArrayHashCodeString(udpPayload)})");
            _socket.Send(udpPayload, udpPayload.Length, remoteEndpoint);
        }
        internal async Task<byte[]> WaitForUdpResponseAsync(PendingLowLevelUdpRequest request)
        {
            if (WriteToLog_udp_deepDetail_enabled) WriteToLog_udp_deepDetail($"waiting for response to {request}");
            _pendingLowLevelUdpRequests.AddLast(request);
            if (_pendingLowLevelUdpRequests.Count > 20)
                WriteToLog_udp_lightPain($"_pendingLowLevelUdpRequests.Count={_pendingLowLevelUdpRequests.Count}");
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
                    if (request.RetransmissionsCount < 2) WriteToLog_udp_detail($"retransmitting request {request}. {request.RetransmissionsCount} retransmissions");
                    else WriteToLog_udp_lightPain($"retransmitting request {request}. {request.RetransmissionsCount} retransmissions");
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

                if (WriteToLog_udp_deepDetail_enabled)
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

        #region responders to retransmitted requests
        /// <summary>
        /// accessed by engine thread only 
        /// </summary>
        Dictionary<RequestKey, ResponderToRetransmittedRequests> _respondersToRetransmittedRequests = new Dictionary<RequestKey, ResponderToRetransmittedRequests>();
        /// <summary>
        /// accessed by engine thread only 
        /// </summary>
        LinkedList<ResponderToRetransmittedRequests> _respondersToRetransmittedRequestsOldestFirst = new LinkedList<ResponderToRetransmittedRequests>();

        /// <summary>
        /// sends response to requests
        /// creates an object "ResponderToRetransmittedRequests" which handles further restransmitted requests (in case if the sent response gets lost)
        /// </summary>
        internal void RespondToRequestAndRetransmissions(byte[] requestUdpPayloadData, byte[] responseUdpPayloadData, IPEndPoint requesterEndpoint)
        {
            SendPacket(responseUdpPayloadData, requesterEndpoint);

            var key = new RequestKey(requestUdpPayloadData, requesterEndpoint);
            var responder = new ResponderToRetransmittedRequests
            {
                RequestKey = key,
                ExpirationTimeUTC = DateTimeNowUtc + Configuration.ResponderToRetransmittedRequestsTimeout,
                ResponseUdpPayloadData = responseUdpPayloadData
            };

            _respondersToRetransmittedRequests.Remove(key); // remove in case if responder s=to same request is already added
            _respondersToRetransmittedRequests.Add(key, responder);
            _respondersToRetransmittedRequestsOldestFirst.AddLast(responder);

            if (_respondersToRetransmittedRequests.Count > 200)
                WriteToLog_udp_lightPain($"_respondersToRetransmittedRequests.Count={_respondersToRetransmittedRequests.Count}");
        }


        bool RespondersToRetransmittedRequests_ProcessPacket(IPEndPoint requesterEndpoint, byte[] udpData)
        {
            var key = new RequestKey(udpData, requesterEndpoint);
            if (_respondersToRetransmittedRequests.TryGetValue(key, out var responder))
            {
                SendPacket(responder.ResponseUdpPayloadData, requesterEndpoint);
                return true;
            }
            else return false;
        }

        void RespondersToRetransmittedRequests_OnTimer100ms(DateTime timeNowUTC)
        {
            for (var item = _respondersToRetransmittedRequestsOldestFirst.First; item != null;)
            {
                var request = item.Value;
                if (timeNowUTC > request.ExpirationTimeUTC)
                {
                    var nextItem = item.Next;
                    _respondersToRetransmittedRequestsOldestFirst.Remove(item);
                    _respondersToRetransmittedRequests.Remove(request.RequestKey);
                    item = nextItem;
                    continue;
                }
                break; // all other items in list are newer, so we can break the enumeration
            }
        }
        #endregion
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
                r += $", req={(PacketTypes)RequestPacketDataNullable[0]}";
            if (ResponseScanner != null && ResponseScanner.ResponseFirstBytes != null)
                r += $", resp={(PacketTypes)ResponseScanner.ResponseFirstBytes[0]}";
            r += $", timeout={_expirationTimeoutS}s";
            return r;
        }
        public int RetransmissionsCount { get; private set; }
        public void OnRetransmitted()
        {
            if (NextRetransmissionTimeUTC == null) throw new InvalidOperationException();
            _currentRetransmissionTimeoutS *= _retransmissionTimeoutIncrement;
            NextRetransmissionTimeUTC = NextRetransmissionTimeUTC.Value.AddSeconds(_currentRetransmissionTimeoutS.Value);
            RetransmissionsCount++;
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
        public RequestKey RequestKey;
        public byte[] ResponseUdpPayloadData;
        public DateTime ExpirationTimeUTC;
        public override string ToString() => $"key={RequestKey}: {MiscProcedures.ByteArrayToString(ResponseUdpPayloadData)}";
    }
    class RequestKey
    {
        public readonly byte[] RequestData;
        public readonly IPEndPoint RequesterEndpoint;
        readonly int _hashCode;
        public RequestKey(byte[] requestData, IPEndPoint requesterEndpoint)
        {
            RequestData = requestData;
            RequesterEndpoint = requesterEndpoint;
            _hashCode = MiscProcedures.GetArrayHashCode(requestData) ^ requesterEndpoint.GetHashCode();
        }
        public override int GetHashCode()
        {
            return _hashCode;
        }
        public override bool Equals(object obj)
        {
            var obj2 = (RequestKey)obj;
            return obj2.RequesterEndpoint.Equals(this.RequesterEndpoint) && MiscProcedures.EqualByteArrays(obj2.RequestData, this.RequestData);
        }
        public override string ToString() => $"{RequesterEndpoint}-{MiscProcedures.ByteArrayToString(RequestData)}";
    }
}