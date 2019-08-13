using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{
    partial class DrpPeerEngine
    {

        void ProcessRegisterPow1RequestPacket(IPEndPoint remoteEndpoint, byte[] udpPayloadData)
        {
            var packet = new RegisterPow1RequestPacket(reader, udpPayloadData);
            if (!PassPoW1filter(clientEndpoint, packet))
                return;

            // create snonce0 state
            var snonce0 = _snonce0Table.GenerateOrGetExistingSnonce0(TimeSec32UTC, clientEndpoint);

            var response = new RegisterPow1ResponsePacket
            {
                Cnonce0 = packet.Cnonce0,
                Snonce0 = snonce0.Snonce0,
                Status = ServerHello0Status.OK,
                StatefulProofOfWorkType = StatefulProofOfWorkType._2019_06
            };
            var responseBytes = response.Encode();
            _ccpTransport.SendPacket(clientEndpoint, responseBytes);
        }


        bool PassPoW1filter(ICcpRemoteEndpoint clientEndpoint, RegisterPow1RequestPacket packet)// packets processor thread // sends responses 
        {
            switch (packet.StatelessProofOfWorkType)
            {
                case StatelessProofOfWorkType._2019_06:
                    // verify size of PoW data
                    if (packet.StatelessProofOfWorkData.Length < 12 || packet.StatelessProofOfWorkData.Length > 64)
                        throw new CcpBadPacketException();

                    // verify datetime ("period")
                    // return err code if time is wrong, with correct server's UTC time
                    uint receivedTimeSec32;

                    unsafe
                    {
                        fixed (byte* statelessProofOfWorkDataPtr = packet.StatelessProofOfWorkData)
                        {
                            fixed (byte* addressBytesPtr = clientEndpoint.AddressBytes)
                            {
                                receivedTimeSec32 = *((uint*)statelessProofOfWorkDataPtr);
                                if (addressBytesPtr[0] != statelessProofOfWorkDataPtr[4] ||
                                    addressBytesPtr[1] != statelessProofOfWorkDataPtr[5] ||
                                    addressBytesPtr[2] != statelessProofOfWorkDataPtr[6] ||
                                    addressBytesPtr[3] != statelessProofOfWorkDataPtr[7]
                                    )
                                {
                                    if (_config.RespondErrors) RespondToHello0(clientEndpoint, ServerHello0Status.ErrorBadStatelessProofOfWork_BadSourceIp, packet.Cnonce0);
                                    return false;
                                }
                            }
                        }
                    }


                    var localTimeSec32 = TimeSec32UTC;
                    var diffSec = Math.Abs((int)unchecked(localTimeSec32 - receivedTimeSec32));
                    if (diffSec > _config.StatelessPoW_MaxClockDifferenceS)
                    {
                        // respond with error "try again with valid clock" - legitimate user has to get valid clock from some time server and synchronize itself with the server
                        if (_config.RespondErrors) RespondToHello0(clientEndpoint, ServerHello0Status.ErrorBadStatelessProofOfWork_BadClock, packet.Cnonce0);
                        return false;
                    }

                    var hash = _cryptoLibrary.GetHashSHA256(packet.OriginalPacketPayload);
                    // calculate hash, considering entire packet data (including stateless PoW result)
                    // verify hash result
                    if (!StatelessPowHashIsOK(hash))
                    {
                        HandleBadStatelessPowPacket(clientEndpoint);
                        // no response
                        return false;
                    }

                    // check if hash is unique
                    var dataIsUnique = _recentUniquePowData.TryInputData(hash, localTimeSec32);

                    if (dataIsUnique)
                    {
                        return true;
                    }
                    else
                    {
                        // respond with error "try again with unique PoW data"
                        if (_config.RespondErrors) RespondToHello0(clientEndpoint, ServerHello0Status.ErrorTryAgainRightNowWithThisServer, packet.Cnonce0);
                        return false;
                    }
                default:
                    throw new CcpBadPacketException();
            }
        }

    }
}
