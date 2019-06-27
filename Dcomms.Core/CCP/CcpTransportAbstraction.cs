using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.CCP
{
    /// <summary>
    /// implementation of transport protocol for the CCP: UDP, DRP, TCP, TLS (in case of additional RLS security)
    /// has socket receiver thread(s) inside and (UDP socket / DRP node)
    /// </summary>
    interface ICcpTransport : IDisposable
    {
        void SendPacket(ICcpRemoteEndpoint remoteEndpoint, byte[] data);
    }
    interface ICcpTransportUser
    {
        void ProcessPacket(ICcpRemoteEndpoint remoteEndpoint, byte[] data);
        void HandleExceptionInCcpReceiverThread(Exception exc);
    }

    interface ICcpRemoteEndpoint // for UDP: remote endpoint IP:port
    {
        string AsString { get; }
        byte[] AddressBytes { get; }
    }
  
}
