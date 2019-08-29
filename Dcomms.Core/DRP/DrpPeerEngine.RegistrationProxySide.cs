using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    partial class DrpPeerEngine
    {
        async Task ProxyRegisterRequestAtEntryPeerAsync(ConnectionToNeighbor destinationPeer, RegisterSynPacket syn, IPEndPoint requesterEndpoint) // engine thread
        {
            WriteToLog_reg_proxySide_detail($"proxying registration at EP: remoteEndpoint={requesterEndpoint}, NhaSeq16={syn.NhaSeq16}, destinationPeer={destinationPeer}");


            _pendingRegisterRequests.Add(syn.RequesterPublicKey_RequestID);
            try
            {
                if (syn.AtoEP == false)
                {
                    //todo check hmac of proxy sender
                    throw new NotImplementedException();
                }

                syn.NumberOfHopsRemaining--;
                if (syn.NumberOfHopsRemaining == 0)
                {
                    SendNextHopAckResponseToSyn(syn, requesterEndpoint, NextHopResponseCode.rejected_numberOfHopsRemainingReachedZero);
                    return;
                }

                // send NHACK to requester
                SendNextHopAckResponseToSyn(syn, requesterEndpoint);


                // send SYN to destinationPeer. wait for NHACK, retransmit SYN
                syn.SenderToken32 = destinationPeer.RemotePeerToken32;
                await destinationPeer.SendUdpRequestAsync_Retransmit_WaitForNHACK(syn.Encode(destinationPeer), syn.NhaSeq16);

                // wait for SYNACK from destinationPeer
                // respond with NHACK

                // send SYNACK to requester
                // wait for ACK / NHACK

                // send ACK to destinationPeer
                // wait for NHACK

                // wait for CFM from requester
                // verify signatures and update quality/rating
                // send NHACK to requester
                // send CFM to destinationPeer
                // wait for NHACK from destinationPeer, retransmit

            }
            catch (Exception exc)
            {
                HandleExceptionWhileProxying(requesterEndpoint, exc);
            }
            finally
            {
                _pendingRegisterRequests.Remove(syn.RequesterPublicKey_RequestID);
            }
        }
    }
}
