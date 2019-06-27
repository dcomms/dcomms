using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Dcomms.CCP
{
    class CcpUdpTransport: ICcpTransport
    {
        bool _disposing;
        Thread _receiverThread;
        UdpClient _socket;
        readonly ICcpTransportUser _user;
        public CcpUdpTransport(ICcpTransportUser user, int? localPort = null)
        {
            _user = user;
            _socket = new UdpClient(localPort ?? 0);
            _receiverThread = new Thread(ThreadEntry);
            _receiverThread.Name = "CCP/UDP receiver";
            _receiverThread.Priority = ThreadPriority.Highest;
            _receiverThread.Start();
        }
        void ThreadEntry()
        {
            IPEndPoint remoteEndpoint = default(IPEndPoint);
            while (!_disposing)
            {
                try
                {
                    var udpPayloadData = _socket.Receive(ref remoteEndpoint);
                    _user.ProcessPacket(new CcpUdpRemoteEndpoint(remoteEndpoint), udpPayloadData);
                }
                catch (SocketException exc)
                {
                    if (_disposing) return;
                    if (exc.ErrorCode != 10054) // forcibly closed - ICMP port unreachable - it is normal when peer gets down
                        _user.HandleExceptionInCcpReceiverThread(exc);
                    // else ignore it
                }
                catch (Exception exc)
                {
                    _user.HandleExceptionInCcpReceiverThread(exc);
                }
            }
        }
        void IDisposable.Dispose()
        {
            if (_disposing) throw new InvalidOperationException();
            _disposing = true;

            _socket.Close();
            _socket.Dispose();

            _receiverThread.Join();
        }
        void ICcpTransport.SendPacket(ICcpRemoteEndpoint remoteEndpoint, byte[] data)
        {
            var ep = (CcpUdpRemoteEndpoint)remoteEndpoint;
            _socket.Send(data, data.Length, ep.Endpoint);
        }
    }
    class CcpUdpRemoteEndpoint: ICcpRemoteEndpoint
    {
        public readonly IPEndPoint Endpoint;
        /// <summary>
        /// makes synchronous DNS request
        /// </summary>
        public CcpUdpRemoteEndpoint(CcpUrl url)
        {
            var address = Dns.GetHostAddresses(url.Host).FirstOrDefault(addr => addr.AddressFamily == AddressFamily.InterNetwork);
            if (address == null) throw new ArgumentException($"can not resolve host '{url.Host}'");
            Endpoint = new IPEndPoint(address, url.Port);
        }
        public CcpUdpRemoteEndpoint(IPEndPoint endpoint)
        {
            Endpoint = endpoint;
        }
        public string AsString => Endpoint.ToString();
        
        byte[] ICcpRemoteEndpoint.AddressBytes => Endpoint.Address.GetAddressBytes();

        public override string ToString() => AsString;
        public override bool Equals(object obj)
        {
            return this.Endpoint.Equals(((CcpUdpRemoteEndpoint)obj).Endpoint);
        }
        public override int GetHashCode() => Endpoint.GetHashCode();
    }
}
