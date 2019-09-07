
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
        private byte Flags;
        public bool RoleFlagIsUser => (Flags & (byte)0x01) != 0;
        public bool FlagIdontNeedMoreSharedPeers => (Flags & (byte)0x02) != 0;
        public bool FlagIwantToGetYourIpLocation => (Flags & (byte)0x04) != 0; // request
        public bool FlagIshareMyIpLocation // response // the flag is set automatically when packet is encoded, from (IpLocationData != null)
        {
            get
            {
                return (Flags & (byte)0x08) != 0;
            }
            set
            {
                if (value) Flags |= 0x08;
                else Flags &= 0xF7;
            }
        }
        public readonly uint RequestTime32;
        public readonly string[] ExtensionIds; // nullable  // not null only for requests
        public readonly ushort? RequestSequenceNumber; // not null after version 190608
        /// <summary>
        /// not null after version 190608
        /// is non-zero only for "accepted" responses
        /// known minimal delay between received request and transmitted response. does not include delays in NIC drivers, in windows UDP/IP stack, in UDP receiver queue
        /// </summary>
        public readonly ushort? ResponseCpuDelayMs;
        public readonly string RequestedFromIp; // is set in responses
        public IpLocationData IpLocationData { get; private set; } // not null if FlagIshareMyIpLocation == true

        /// <summary>
        /// creates request, for transmission to peer
        /// </summary>
        /// <param name="connectedPeer">destination</param>
        /// <param name="stream">destination</param>
        public PeerHelloPacket(LocalPeer localPeer, ConnectedPeer connectedPeer, ConnectedPeerStream stream, PeerHelloRequestStatus status, bool requestIpLocation)
        {
            LibraryVersion = MiscProcedures.CompilationDateTimeUtc_uint32Seconds;
            ProtocolVersion = P2ptpCommon.ProtocolVersion;
            FromPeerId = localPeer.LocalPeerId;
            ExtensionIds = localPeer.Configuration.Extensions?.Select(x => x.ExtensionId).ToArray();
            StreamId = stream.StreamId;
            ToPeerId = connectedPeer.RemotePeerId;
            Status = status;
            RequestTime32 = localPeer.Time32;
            if (localPeer.Configuration.RoleAsUser) Flags |= (byte)0x01;
            if (requestIpLocation) Flags |= (byte)0x04;
        }

        /// <summary>
        /// creates packet for response
        /// </summary>
        private PeerHelloPacket(PeerHelloPacket requestPacket, PeerHelloRequestStatus status, PeerId localPeerId, bool thisPeerRoleAsUser, ushort? responseCpuDelayMs, string requestedFromIp)
        {
            LibraryVersion = MiscProcedures.CompilationDateTimeUtc_uint32Seconds;
            ProtocolVersion = P2ptpCommon.ProtocolVersion;
            FromPeerId = localPeerId ?? requestPacket.ToPeerId; 
            ToPeerId = requestPacket.FromPeerId;
            StreamId = requestPacket.StreamId;
            Status = status;
            RequestTime32 = requestPacket.RequestTime32;
            Flags = thisPeerRoleAsUser ? (byte)0x01 : (byte)0x00;
            RequestSequenceNumber = requestPacket.RequestSequenceNumber;
            ResponseCpuDelayMs = responseCpuDelayMs;
            RequestedFromIp = requestedFromIp;
        } 
        /// <summary>
        /// creates response to request and sends the response
        /// </summary>
        /// <param name="localPeerId">optional local test node ID, is sent by server to new peers who dont know server's PeerId</param>
        internal static void Respond(PeerHelloPacket requestPacket, PeerHelloRequestStatus status, PeerId localPeerId, 
            SocketWithReceiver socket, IPEndPoint remoteEndPoint, ushort? responseCpuDelayMs = null, bool thisPeerRoleAsUser = false, IpLocationData localIpLocationData = null)
        {
            var responsePacket = new PeerHelloPacket(requestPacket, status, localPeerId, thisPeerRoleAsUser, responseCpuDelayMs, remoteEndPoint.Address.ToString());
            if (localIpLocationData != null && requestPacket.FlagIwantToGetYourIpLocation)
                responsePacket.IpLocationData = localIpLocationData;
            var responseData = responsePacket.Encode();
            socket.UdpSocket.Send(responseData, responseData.Length, remoteEndPoint);
        }

        public PeerHelloPacket(byte[] packetUdpPayloadData)
        {
          //  if (packetUdpPayloadData.Length < MinEncodedSize) throw new ArgumentException(nameof(packetUdpPayloadData));
            var index = P2ptpCommon.HeaderSize;
            FromPeerId = PeerId.Decode(packetUdpPayloadData, ref index);
            StreamId = StreamId.Decode(packetUdpPayloadData, ref index);
            ToPeerId = PeerId.Decode(packetUdpPayloadData, ref index);         
            LibraryVersion = PacketProcedures.DecodeUInt32(packetUdpPayloadData, ref index);
            ProtocolVersion = PacketProcedures.DecodeUInt16(packetUdpPayloadData, ref index);
            Status = (PeerHelloRequestStatus)packetUdpPayloadData[index++];
            RequestTime32 = PacketProcedures.DecodeUInt32(packetUdpPayloadData, ref index);
            Flags = packetUdpPayloadData[index++];
            var extensionIdsLength = packetUdpPayloadData[index++];
            ExtensionIds = new string[extensionIdsLength];
            for (byte i = 0; i < extensionIdsLength; i++)
                ExtensionIds[i] = PacketProcedures.DecodeString1ASCII(packetUdpPayloadData, ref index);
            if (index < packetUdpPayloadData.Length)
            { // after version 190608
                RequestSequenceNumber = PacketProcedures.DecodeUInt16(packetUdpPayloadData, ref index);
                ResponseCpuDelayMs = PacketProcedures.DecodeUInt16(packetUdpPayloadData, ref index);
                RequestedFromIp = PacketProcedures.DecodeString1ASCII(packetUdpPayloadData, ref index);
                var reader = PacketProcedures.CreateBinaryReader(packetUdpPayloadData, index);
                if (FlagIshareMyIpLocation)
                    IpLocationData = IpLocationData.Decode(reader);
            }
        }
        //const int MinEncodedSize = P2ptpCommon.HeaderSize +
        //        PeerId.EncodedSize + StreamId.EncodedSize +
        //        PeerId.EncodedSize + 
        //        4 + 2 + // library, protocol version
        //        1 + // status
        //        4 + // requesttime
        //        1 + // role flags
        //        1; // extensions length
        //const int OptionalEncodedSize = 2 + // RequestSequenceNumber
        //    2 +//ResponseCpuDelayMs
        //    1; // RequestedFromIp length
        public byte[] Encode()
        {
          //  var size = MinEncodedSize;
        //    if (ExtensionIds != null)
        //        foreach (var extensionId in ExtensionIds)
         //           size += 1 + extensionId.Length;
            var requestedFromIp = RequestedFromIp ?? "";
            FlagIshareMyIpLocation = (IpLocationData != null);
            //    size += OptionalEncodedSize + requestedFromIp.Length;

            //  byte[] data = new byte[size];
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            P2ptpCommon.EncodeHeader(writer, PacketType.hello);
          //  int index = P2ptpCommon.HeaderSize;
            PeerId.Encode(writer, FromPeerId);
            StreamId.Encode(writer, StreamId);
            PeerId.Encode(writer, ToPeerId);
            writer.Write(LibraryVersion);
            writer.Write(ProtocolVersion);
            writer.Write((byte)Status);
            writer.Write(RequestTime32);
            writer.Write(Flags);
            writer.Write((byte)(ExtensionIds?.Length ?? 0));
            if (ExtensionIds != null)
                foreach (var extensionId in ExtensionIds)
                    PacketProcedures.EncodeString1ASCII(writer, extensionId);

            writer.Write(RequestSequenceNumber ?? 0);
            writer.Write(ResponseCpuDelayMs ?? 0);
            PacketProcedures.EncodeString1ASCII(writer, requestedFromIp);

            if (FlagIshareMyIpLocation)
                IpLocationData.Encode(writer);
            
            return ms.ToArray();
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
