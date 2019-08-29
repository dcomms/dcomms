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
        async Task ProxyRegisterRequestAtEntryPeerAsync(ConnectionToNeighbor proxyTo, RegisterSynPacket registerSynPacket, IPEndPoint remoteEndpoint) // engine thread
        {
            WriteToLog_reg_proxySide_detail($"proxying registration at EP: remoteEndpoint={remoteEndpoint}, NhaSeq16={registerSynPacket.NhaSeq16}, proxyTo={proxyTo}");


            _pendingRegisterRequests.Add(registerSynPacket.RequesterPublicKey_RequestID);
            try
            {
                if (registerSynPacket.AtoEP == false)
                {
                    //todo check hmac of proxy sender
                    throw new NotImplementedException();
                }




                // assert tx rate is not exceeded  -- return false

                // create an instance of TxRequestState, add it to list

                // send packet to peer
            }
            catch (Exception exc)
            {
                HandleExceptionWhileProxying(remoteEndpoint, exc);
            }
            finally
            {
                _pendingRegisterRequests.Remove(registerSynPacket.RequesterPublicKey_RequestID);
            }
        }
    }
}
