using Dcomms.P2PTP.LocalLogic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Dcomms.P2PTP.Extensibility
{
    /// <summary>
    /// represents connected peer stream for extensions
    /// </summary>
    public interface IConnectedPeerStream
    {
        string LocalRemoteEndPointString { get; } // used for GUI only
        void SendPacket(byte[] data, int length); // called by multiple SUBT TX streams, by manager stream (measurements)
        IDictionary<ILocalPeerExtension, IConnectedPeerStreamExtension> Extensions { get; } 
        StreamId StreamId { get; }
        void MarkAsActiveByExtension(); // = not idle
        string P2ptpActivityString { get; }
        bool Debug { get; set; }
        bool RemotePeerRoleIsUser { get; }
        bool IsIdle(DateTime now, TimeSpan maxIdleTime);
        TimeSpan? LatestHelloRtt { get; }
    }
    /// <summary>
    /// extension-specofoc object linked to connetced peer stream
    /// SUBT: stores measurements 
    /// </summary>
    public interface IConnectedPeerStreamExtension
    {
        /// <summary>
        /// is executed in manager thread.
        /// used for signaling packets, (not frequent) (not payload)
        /// </summary>
        void OnReceivedSignalingPacket(BinaryReader reader);
        
        /// <summary>
        /// processes extension-specific UDP packets, directly in receiver thread
        /// the packet must start with extension-specific header bytes
        /// the 'payload' packets are fastest in terms of CPU load - actual voice, video, data
        /// </summary>
        void OnReceivedPayloadPacket(byte[] data, int index);

        /// <summary>
        /// removes stream from SUBT transmitter thread
        /// </summary>
        void OnDestroyed();
    }
}
