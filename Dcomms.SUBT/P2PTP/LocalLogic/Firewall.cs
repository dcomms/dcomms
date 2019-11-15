using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.P2PTP.LocalLogic
{
    /// <summary>
    /// automatically blacklists IP addresses that send brute force attacks
    /// </summary>
    class Firewall
    {
        internal void OnUnauthenticatedReceivedPacket(IPEndPoint remoteEP) // manager thread
        {
            // todo
        }
        internal void OnReceivedTooManyConnectionsFrom(IPEndPoint remoteEP)
        {

        }
        internal void OnReceivedTooManyPacketsFrom(IPEndPoint remoteEP)
        {

        }
        internal bool PacketIsAllowed(IPEndPoint remoteEndpoint)
        {
            // todo
            return true;
        }
    }
}
