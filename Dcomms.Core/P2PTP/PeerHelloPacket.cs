
using Dcomms.P2PTP.LocalLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Dcomms.P2PTP
{
    /// <summary>
    /// first packet between peers, for every new interconnection of peers
    /// request or response
    /// </summary>
    internal class PeerHelloPacket
    {
        public readonly PeerId FromPeerId;
        public readonly StreamId StreamId;
        /// <summary>
        /// optional    
        /// is null for 'setup' packets to server (when remote testnode is unknown)
        /// must be set for response  to make sure that it is not fake server's response  
        /// must be set for p2p connection requests
        /// </summary>
        public readonly PeerId ToPeerId; 

        public readonly uint LibraryVersion; // of sender peer // see CompilationInfo.ToDateTime()
        public readonly ushort ProtocolVersion; // of sender peer
        public readonly PeerHelloRequestStatus Status; // byte; indicates if it is request or response
        private readonly byte Flags;
        public bool RoleFlagIsUser => (Flags & (byte)0x01) != 0;
        public bool FlagIdontNeedMoreSharedPeers => (Flags & (byte)0x02) != 0;
        public readonly uint RequestTime32;
        public readonly string[] ExtensionIds; // nullable  // not null only for requests
        public readonly ushort? RequestSequenceNumber; // not null after version 190608
        /// <summary>
        /// not null after version 190608
        /// is non-zero only for "accepted" responses
        /// known minimal delay between received request and transmitted response. does not include delays in NIC drivers, in windows UDP/IP stack, in UDP receiver queue
        /// </summary>
        public readonly ushort? ResponseCpuDelayMs;


        /// <summary>
        /// creates request, for transmission to peer
        /// </summary>
        /// <param name="connectedPeer">destination</param>
        /// <param name="stream">destination</param>
        public PeerHelloPacket(LocalPeer localPeer, ConnectedPeer connectedPeer, ConnectedPeerStream stream, PeerHelloRequestStatus status)
        {
            LibraryVersion = MiscProcedures.CompilationDateTimeUtc_uint32;
            ProtocolVersion = P2ptpCommon.ProtocolVersion;
            FromPeerId = localPeer.LocalPeerId;
            ExtensionIds = localPeer.Configuration.Extensions?.Select(x => x.ExtensionId).ToArray();
            StreamId = stream.StreamId;
            ToPeerId = connectedPeer.RemotePeerId;
            Status = status;
            RequestTime32 = localPeer.Time32;
            Flags = localPeer.Configuration.RoleAsUser ? (byte)0x01 : (byte)0x00;
        }

        /// <summary>
        /// creates packet for response
        /// </summary>
        private PeerHelloPacket(PeerHelloPacket requestPacket, PeerHelloRequestStatus status, PeerId localPeerId, bool thisPeerRoleAsUser, ushort? responseCpuDelayMs)
        {
            LibraryVersion = MiscProcedures.CompilationDateTimeUtc_uint32;
            ProtocolVersion = P2ptpCommon.ProtocolVersion;
            FromPeerId = localPeerId ?? requestPacket.ToPeerId; 
            ToPeerId = requestPacket.FromPeerId;
            StreamId = requestPacket.StreamId;
            Status = status;
            RequestTime32 = requestPacket.RequestTime32;
            Flags = thisPeerRoleAsUser ? (byte)0x01 : (byte)0x00;
            RequestSequenceNumber = requestPacket.RequestSequenceNumber;
            ResponseCpuDelayMs = responseCpuDelayMs;
        } 
        /// <summary>
        /// creates response to request and sends the response
        /// </summary>
        /// <param name="localPeerId">optional local test node ID, is sent by server to new peers who dont know server's PeerId</param>
        internal static void Respond(PeerHelloPacket requestPacket, PeerHelloRequestStatus status, PeerId localPeerId, 
            SocketWithReceiver socket, IPEndPoint remoteEndPoint, ushort? responseCpuDelayMs = null, bool thisPeerRoleAsUser = false)
        {
            var responseData = new PeerHelloPacket(requestPacket, status, localPeerId, thisPeerRoleAsUser, responseCpuDelayMs).Encode();
            socket.UdpSocket.Send(responseData, responseData.Length, remoteEndPoint);
        }

        public PeerHelloPacket(byte[] packetUdpPayloadData)
        {
            if (packetUdpPayloadData.Length < MinEncodedSize) throw new ArgumentException(nameof(packetUdpPayloadData));
            var index = P2ptpCommon.HeaderSize;
            FromPeerId = PeerId.Decode(packetUdpPayloadData, ref index);
            StreamId = StreamId.Decode(packetUdpPayloadData, ref index);
            ToPeerId = PeerId.Decode(packetUdpPayloadData, ref index);         
            LibraryVersion = P2ptpCommon.DecodeUInt32(packetUdpPayloadData, ref index);
            ProtocolVersion = P2ptpCommon.DecodeUInt16(packetUdpPayloadData, ref index);
            Status = (PeerHelloRequestStatus)packetUdpPayloadData[index++];
            RequestTime32 = P2ptpCommon.DecodeUInt32(packetUdpPayloadData, ref index);
            Flags = packetUdpPayloadData[index++];
            var extensionIdsLength = packetUdpPayloadData[index++];
            ExtensionIds = new string[extensionIdsLength];
            for (byte i = 0; i < extensionIdsLength; i++)
                ExtensionIds[i] = P2ptpCommon.DecodeString1ASCII(packetUdpPayloadData, ref index);
            if (index < packetUdpPayloadData.Length)
            { // after version 190608
                RequestSequenceNumber = P2ptpCommon.DecodeUInt16(packetUdpPayloadData, ref index);
                ResponseCpuDelayMs = P2ptpCommon.DecodeUInt16(packetUdpPayloadData, ref index);
            }
        }
        const int MinEncodedSize = P2ptpCommon.HeaderSize +
                PeerId.EncodedSize + StreamId.EncodedSize +
                PeerId.EncodedSize + 
                4 + 2 + // library, protocol version
                1 + // status
                4 + // requesttime
                1 + // role flags
                1; // extensions length
        const int OptionalEncodedSize = 2 + // RequestSequenceNumber
            2; //ResponseCpuDelayMs
        public byte[] Encode()
        {
            var size = MinEncodedSize;
            if (ExtensionIds != null)
                foreach (var extensionId in ExtensionIds)
                    size += 1 + extensionId.Length;
            size += OptionalEncodedSize;

            byte[] data = new byte[size];
            P2ptpCommon.EncodeHeader(data, PacketType.hello);
            int index = P2ptpCommon.HeaderSize;
            PeerId.Encode(FromPeerId, data, ref index);
            StreamId.Encode(StreamId, data, ref index);
            PeerId.Encode(ToPeerId, data, ref index);
            P2ptpCommon.EncodeUInt32(data, ref index, LibraryVersion);
            P2ptpCommon.EncodeUInt16(data, ref index, ProtocolVersion);
            data[index++] = (byte)Status;
            P2ptpCommon.EncodeUInt32(data, ref index, RequestTime32);
            data[index++] = Flags;
            data[index++] = (byte)(ExtensionIds?.Length ?? 0);
            if (ExtensionIds != null)
                foreach (var extensionId in ExtensionIds)
                    P2ptpCommon.EncodeString1ASCII(data, ref index, extensionId);

            P2ptpCommon.EncodeUInt16(data, ref index, RequestSequenceNumber ?? 0);
            P2ptpCommon.EncodeUInt16(data, ref index, ResponseCpuDelayMs ?? 0);

            return data;
        }
    }

    enum PeerHelloRequestStatus // byte
    {
        /// <summary>
        /// request
        /// connection setup from client-peer to server-peer
        /// testNodeId is NULL when it is "initial connection" to coordinatorServer
        /// </summary>
        setup = 1,
        /// <summary>
        /// request
        /// is sent periodically by both peers
        /// </summary>
        ping = 2,

        /// <summary>
        /// can't accept connection:
        /// - invalid ToPeerId (this peer is restarted and testNodeId changed)
        /// - invalid StreamId (not unique for new stream)
        /// - invalid source IP and port for existing StreamId
        /// </summary>
        rejected_tryCleanSetup = 100,
        /// <summary>
        /// can't accept connection:
        /// - testNodeId does not match (for p2p connections)
        /// - overload: too many conenctions from this peer
        /// </summary>
        rejected_dontTryLater = 101,
        /// <summary>
        /// can't accept connection: overload: try again later
        /// </summary>
        rejected_tryLater = 102,

        /// <summary>
        /// response to setup: connection is set up
        /// response to ping: connection is kept alive
        /// </summary>
        accepted = 200,
    }
    internal static class PeerHelloRequestStatusExtensions
    {
        public static bool IsSetupOrPing(this PeerHelloRequestStatus s) => (s == PeerHelloRequestStatus.ping || s == PeerHelloRequestStatus.setup);
    }
}
