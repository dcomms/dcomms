using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{
    public class Firewall
    {
        public class SenderInfo
        {
            public IPAddress IpAddress { get; set; }
            public DateTime FirstPacketTimeUtc { get; set; }
            public DateTime LastPacketTimeUtc { get; set; }
            public SenderInfo()
            {

            }
            public SenderInfo(SenderInfo copyFrom)
            {
                this.IpAddress = copyFrom.IpAddress;
                this.FirstPacketTimeUtc = copyFrom.FirstPacketTimeUtc;
                this.LastPacketTimeUtc = copyFrom.LastPacketTimeUtc;
            }
            public void AggregateWith(SenderInfo other)
            {
                if (other.FirstPacketTimeUtc < this.FirstPacketTimeUtc) this.FirstPacketTimeUtc = other.FirstPacketTimeUtc;
                if (other.LastPacketTimeUtc > this.LastPacketTimeUtc) this.LastPacketTimeUtc = other.LastPacketTimeUtc;
            }
        }
        Dictionary<IPAddress, SenderInfo> _senders = new Dictionary<IPAddress, SenderInfo>(); // locked
        public List<SenderInfo> Senders
        {
            get
            {
                lock (_senders)
                {
                    return _senders.Values.ToList();
                }
            }
        }
        public bool PassReceivedPacket(DateTime dateTimeNowUtc, IPEndPoint remoteEndpoint) // receiver thread
        {
            lock (_senders)
            {
                if (_senders.TryGetValue(remoteEndpoint.Address, out var existingSender))
                {
                    existingSender.LastPacketTimeUtc = dateTimeNowUtc;
                }
                else
                {
                    _senders.Add(remoteEndpoint.Address, new SenderInfo
                    {
                        FirstPacketTimeUtc = dateTimeNowUtc,
                        LastPacketTimeUtc = dateTimeNowUtc,
                        IpAddress = remoteEndpoint.Address
                    });
                }
                while (_senders.Count > 10000)
                {
                    var oldestLastPacketTimeUtc = _senders.Values.Min(p => p.LastPacketTimeUtc);
                    var oldestSender = _senders.Values.First(x => x.LastPacketTimeUtc == oldestLastPacketTimeUtc);
                    _senders.Remove(oldestSender.IpAddress);
                }
            }
            return true;
        }
    }
}
