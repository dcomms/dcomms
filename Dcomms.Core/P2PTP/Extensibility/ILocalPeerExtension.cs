using Dcomms.P2PTP.LocalLogic;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.P2PTP.Extensibility
{
    /// <summary>
    /// interface of P2PTP extension, a higher-level application protocol running over P2PTP (aka plugin),
    /// to the P2PTS core
    /// </summary>
    public interface ILocalPeerExtension
    {
        /// <summary>
        /// identifies protocol that runs over P2PTP. is used in peersList and extensionSignaling packets
        /// contains one of predefined prefixes and version
        /// max 8 characters long
        /// first 4 digits: "compatibility": used to share peers efficiently, with peersList packet
        /// last 4 digits, optional: "version" of the extension
        /// </summary>
        string ExtensionId { get; }
        /// <summary>
        /// nullable
        /// starting byte(s) in UDP packet, used by receiver thread to find corresponding extension 
        /// </summary>
        byte[] PayloadPacketHeader { get; }

        void ReinitializeWithLocalPeer(ILocalPeer localPeer);
        void DestroyWithLocalPeer();
        IConnectedPeerExtension OnConnectedPeer(IConnectedPeer connectedPeer);
        void OnTimer100msApprox(); // manager thread 
    }
    /// <summary>
    /// used to share peers efficiently, with peersList packet
    /// globally registers known extensions
    /// </summary>
    public static class ExtensionIdPrefixes
    {
        public const string SUBT = "SUBT";
        /// <summary>
        /// unknown (is not declared here)
        /// </summary>
        public const string X = "X";
    }
    /// <summary>
    /// represents local peer for extensions
    /// </summary>
    public interface ILocalPeer
    {
        /// <summary>
        /// connected peers who sent "hello" with matching etensionId
        /// </summary>
        IConnectedPeer[] ConnectedPeers { get; }
        void HandleException(ILocalPeerExtension extension, Exception exception);
        void WriteToLog(ILocalPeerExtension extension, string message);
        /// <summary>
        /// is used for timestamp fields
        /// TimeSpan ticks
        /// </summary>
        uint Time32 { get; }
        long Time64 { get; }
        Random Random { get; }
        PeerId LocalPeerId { get; }
        LocalPeerConfiguration Configuration { get; }
        DateTime DateTimeNowUtc { get; }
        DateTime DateTimeNow { get; }
        void InvokeInManagerThread(Action a);
    }
}
