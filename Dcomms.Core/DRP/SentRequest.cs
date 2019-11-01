using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    /// <summary>
    /// sends and retransmits requestUdpData  until 
    /// 1) NPACK-accepted, ACK1 
    /// OR
    /// 2) NPACK-accepted, FAILURE, (sent) NPACK 
    /// OR
    /// 3) NPACK-failure
    /// 
    /// sends NPACK to FAILURE
    /// 
    /// throws RequestRejectedException/RequestRejectedExceptionRouteIsUnavailable/DrpTimeoutException exception
    /// 
    /// stores result: ACK1 UDP data
    /// </summary>
    class SentRequest
    {
        readonly byte[] _requestUdpData;
        readonly RequestP2pSequenceNumber16 _sentReqP2pSeq16;
        readonly LowLevelUdpResponseScanner _ack1Scanner;
        readonly ConnectionToNeighbor _destinationNeighborNullable; // is null in A-EP mode at A
        IPEndPoint _destinationEndpoint;
        readonly DrpPeerEngine _engine;
        readonly Logger _logger;

        public byte[] Ack1UdpData; // result of the request
        byte[] _failureUdpData;

        public SentRequest(DrpPeerEngine engine, Logger logger, IPEndPoint destinationEndpoint, ConnectionToNeighbor destinationNeighborNullable, byte[] requestUdpData, 
            RequestP2pSequenceNumber16 sentReqP2pSeq16, LowLevelUdpResponseScanner ack1Scanner)
        {
            _destinationEndpoint = destinationEndpoint;
            _logger = logger;
            _requestUdpData = requestUdpData;
            _sentReqP2pSeq16 = sentReqP2pSeq16;
            _ack1Scanner = ack1Scanner;
            _destinationNeighborNullable = destinationNeighborNullable;
            _engine = engine;
        }

        public async Task SendRequestAsync()
        {
            // wait for NPACK (-accepted or -failure)
            await _engine.OptionallySendUdpRequestAsync_Retransmit_WaitForNeighborPeerAck(_requestUdpData, _destinationEndpoint, _sentReqP2pSeq16);
            _logger.WriteToLog_detail($"received NPACK");
          
            // wait for ACK1 OR FAILURE
            await Task.WhenAny(
                WaitForAck1Async(),
                WaitForFailureAsync()
                );
            if (_WaitForAck1Completed)
            {
                _logger.WriteToLog_detail($"received ACK1");
                if (Ack1UdpData == null) throw new DrpTimeoutException();
            }
            else if (_WaitForFailureCompleted)
            {
                if (_failureUdpData == null) throw new DrpTimeoutException();
                _logger.WriteToLog_detail($"received FAILURE");
                var failure = FailurePacket.DecodeAndOptionallyVerify(_failureUdpData, _sentReqP2pSeq16);
                
                if (_failureUdpData != null)
                {
                    // send NPACK to FAILURE
                    var npAckToFailure = new NeighborPeerAckPacket
                    {
                        ReqP2pSeq16 = _sentReqP2pSeq16,
                        ResponseCode = ResponseOrFailureCode.accepted
                    };
                    if (_destinationNeighborNullable != null)
                    {
                        npAckToFailure.NeighborToken32 = _destinationNeighborNullable.RemoteNeighborToken32;
                        npAckToFailure.NeighborHMAC = _destinationNeighborNullable.GetNeighborHMAC(w => npAckToFailure.GetSignedFieldsForNeighborHMAC(w, failure.GetSignedFieldsForNeighborHMAC));
                    }

                    var npAckUdpData = npAckToFailure.Encode(_destinationNeighborNullable == null);

                    _engine.RespondToRequestAndRetransmissions(_failureUdpData, npAckUdpData, _destinationEndpoint);
                }
            }
            else throw new InvalidOperationException();
         
        }
        bool _WaitForAck1Completed;
        async Task WaitForAck1Async()
        {
            _logger.WriteToLog_detail($"waiting for ACK1");
            Ack1UdpData = await _engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(_destinationEndpoint,
                            _ack1Scanner, _engine.DateTimeNowUtc, _engine.Configuration.Ack1TimoutS
                            ));
            _WaitForAck1Completed = true;
        }

        bool _WaitForFailureCompleted;
        async Task WaitForFailureAsync()
        {
            var failureScanner = FailurePacket.GetScanner(_logger, _sentReqP2pSeq16, _destinationNeighborNullable); // the scanner verifies neighborHMAC
            _logger.WriteToLog_detail($"waiting for FAILURE");
            _failureUdpData = await _engine.WaitForUdpResponseAsync(new PendingLowLevelUdpRequest(_destinationEndpoint,
                            failureScanner, _engine.DateTimeNowUtc, _engine.Configuration.Ack1TimoutS
                            ));
            _WaitForFailureCompleted = true;           
        }

    }
}
