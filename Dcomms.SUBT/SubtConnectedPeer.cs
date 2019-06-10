using Dcomms.P2PTP;
using Dcomms.DSP;
using Dcomms.P2PTP.Extensibility;
using Dcomms.SUBT.SUBTP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dcomms.SUBT
{
    public class SubtConnectedPeer : IConnectedPeerExtension
    {
        readonly SubtLocalPeer _subtLocalPeer;
        readonly IConnectedPeer _connectedPeer;
        public PeerId RemotePeerId => _connectedPeer.RemotePeerId;
        public DateTime? RemoteLibraryVersion => _connectedPeer.RemoteLibraryVersion;
        public string RemoteIpLocationString
        {
            get
            {
                var l = _connectedPeer.RemoteIpLocationData;
                if (l == null) return "";
                return $"{l.Country}, {l.State}, {l.City}";
            }
        }
        public string RemoteIp => _connectedPeer.RemoteIp;
        public string BestStreamsRttString
        {
            get
            {
                TimeSpan? bestRtt = null;
                foreach (var s in Streams)
                {
                    var rtt = s.RecentRttConsideringP2ptp;
                    if (bestRtt == null || rtt < bestRtt.Value)
                        bestRtt = rtt;
                }
                if (bestRtt == null) return "";
                return MiscProcedures.TimeSpanToString(bestRtt);
            }
        }
        public ConnectedPeerType Type => _connectedPeer.Type;

        public string LatestRemoteTxStatusString => Streams.Sum(s => s.LatestRemoteStatus?.RecentTxBandwidth ?? 0).BandwidthToString();
        public string LatestRemoteRxStatusString => Streams.Sum(s => s.LatestRemoteStatus?.RecentRxBandwidth ?? 0).BandwidthToString();
        public float TargetTxBandwidth
        {
            get { return Streams.Sum(x => x.TargetTxBandwidth); }          
        }
        public string TargetTxBandwidthString => TargetTxBandwidth.BandwidthToString();


        public float RecentTxBandwidth => Streams.Sum(x => x.RecentTxBandwidth);
        public string RecentTxBandwidthString => RecentTxBandwidth.BandwidthToString();

        public float RecentRxBandwidth => Streams.Sum(x => x.RecentRxBandwidth);
        public string RecentRxBandwidthString => RecentRxBandwidth.BandwidthToString();

        internal SubtConnectedPeer(SubtLocalPeer subtLocalPeer, IConnectedPeer connectedPeer)
        {
            _connectedPeer = connectedPeer;
            _subtLocalPeer = subtLocalPeer;
        }
        public override string ToString()
        {
            return $"{RemotePeerId}:targetTxBw={TargetTxBandwidth.BandwidthToString()}";
        }
        IConnectedPeerStreamExtension IConnectedPeerExtension.OnConnectedPeerStream(IConnectedPeerStream stream)
        {
            return new SubtConnectedPeerStream(stream, _subtLocalPeer, this);
        }
        public List<SubtConnectedPeerStream> StreamsAsList => Streams.ToList(); // needed to edit values in WPF GUI
        public IEnumerable<SubtConnectedPeerStream> Streams 
        {
            get
            {
                foreach (var s in _connectedPeer.Streams)
                {
                    if (s.Extensions.TryGetValue(_subtLocalPeer, out var sx))
                        yield return (SubtConnectedPeerStream)sx;
                }
            }
        }
        public bool StreamsExpanded { get; set; }

    }
}
