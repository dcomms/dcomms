using Dcomms.P2PTP.LocalLogic;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.P2PTP.Extensibility
{
    /// <summary>
    /// represents connected peer for extensions
    /// </summary>
    public interface IConnectedPeer
    {
        IConnectedPeerStream[] Streams { get; }
        /// <summary>
        /// is null for 'pending' peers, when no signaling and payload packets can be sent (not handshaked - pending peer conenction to coordinator when remotePeerId is unknown)
        /// </summary>
        PeerId RemotePeerId { get; }
        IDictionary<ILocalPeerExtension, IConnectedPeerExtension> Extensions { get; }
        ConnectedPeerType Type { get; }
    }
    /// <summary>
    /// represents extension-specific object linked to connected peer
    /// stores SUBT measurements (global per connected peer, joint from all streams)
    /// </summary>
    public interface IConnectedPeerExtension
    {
        /// <summary>
        /// adds stream to SUBT transmitter thread
        /// </summary>
        IConnectedPeerStreamExtension OnConnectedPeerStream(IConnectedPeerStream stream);
    }


    public enum ConnectedPeerType
    {
        toConfiguredServer,
        fromPeerAccepted,
        toPeerShared // made new conenction because received "peersList" packet
    }
}
