using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.CCP
{
    enum CcpPacketType
    {
        ClientHelloPacket0 = 0,
        ServerHelloPacket0 = 1,
        ClientHelloPacket1 = 2,
        ServerHelloPacket1 = 3,
        ClientPingRequestPacket = 4,
        ServerPingResponsePacket = 5,
    }

    // ======================================================================= hello0 stage =====================================================================================================

    /// <summary>
    /// very first packet in handshaking sent from client to server
    /// </summary>
    public class ClientHelloPacket0
    {
        public ushort Flags; // reserved // cipher suites
        public byte[] Cnonce0; // acts as cnonce (temporary value) and Diffie-Hellman exchange data  // to avoid conflicts between instances // to avoid server spoofing, source for server's signature
        public const int Cnonce0SupportedSize = 8; // only 1 size now - 201906
        public StatelessProofOfWorkType StatelessProofOfWorkType;
        public byte[] StatelessProofOfWorkData;
        byte[] ClientSessionPublicKey;
        byte[] ClientCertificate; 
        byte[] ClientSignature; // set if client is registered
       
        public ClientHelloPacket0()
        {
        }
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            Encode(writer);
            return ms.ToArray();
        }
        /// <returns>offset to StatelessProofOfWorkData</returns>
        public int Encode(BinaryWriter writer)
        {
            writer.Write((byte)CcpPacketType.ClientHelloPacket0);
            writer.Write(Flags);
            if (Cnonce0.Length != Cnonce0SupportedSize) throw new CcpBadPacketException();
            PacketProcedures.EncodeByteArray256(writer, Cnonce0);
            writer.Write((byte)StatelessProofOfWorkType);
          
            PacketProcedures.EncodeByteArray256(writer, StatelessProofOfWorkData);
            return 1 + 2 + 1 + Cnonce0.Length + 1 + 1;
        }
        public readonly byte[] OriginalPacketPayload;
        public ClientHelloPacket0(BinaryReader reader, byte[] originalPacketPayload) // after first byte = packet type
        {
            OriginalPacketPayload = originalPacketPayload;
            Flags = reader.ReadUInt16();
            Cnonce0 = PacketProcedures.DecodeByteArray256(reader);
            if (Cnonce0.Length != Cnonce0SupportedSize) throw new CcpBadPacketException();
            StatelessProofOfWorkType = (StatelessProofOfWorkType)reader.ReadByte();
            StatelessProofOfWorkData = PacketProcedures.DecodeByteArray256(reader);
        }
    }
    public enum StatelessProofOfWorkType
    {
        none = 0,
        _2019_06 = 1, // sha256(pow_data||rest_packet_data||time_now) has bytes 4..6 set to 7;  al
    }

    /// <summary> 
    /// second packet in the handshaking, sent from server to client
    /// after sending this packet, server creates a Snonce0 state
    /// </summary>
    class ServerHelloPacket0
    {
        public ushort Flags; //reserved
        public ServerHello0Status Status;
        public byte[] Cnonce0; // must be reflected by server

        // following fields are set if status = OK 
        public StatefulProofOfWorkType StatefulProofOfWorkType;
        public byte[] Snonce0; // = Stateful Proof Of Work (#1) Request Data

        public const int Snonce0SupportedSize = 32;

        byte[] ServerHelloToken; // acts as snonce and Diffie-Hellman exchange data
        byte[] ServerCertificate; // optional intermediate certificate
        string UnencryptedFallbackServers; // to be used in case of errors while crypto channel is unavailable
        byte[] ServerSessionPublicKey; // is set only if server is sure that it is not DoS
        byte[] ServerSignature; // is set only if server is sure that it is not DoS
        
        public ServerHelloPacket0()
        {
        }
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            writer.Write((byte)CcpPacketType.ServerHelloPacket0);
            writer.Write(Flags);
            writer.Write((byte)Status);
            PacketProcedures.EncodeByteArray256(writer, Cnonce0);

            if (Status == ServerHello0Status.OK)
            {
                writer.Write((byte)StatefulProofOfWorkType);
                PacketProcedures.EncodeByteArray256(writer, Snonce0);
            }
            return ms.ToArray();
        }
        public ServerHelloPacket0(byte[] udpPayloadData)
        {
            var reader = PacketProcedures.CreateBinaryReader(udpPayloadData, 1);
            Flags = reader.ReadUInt16();
            Status = (ServerHello0Status)reader.ReadByte();
            Cnonce0 = PacketProcedures.DecodeByteArray256(reader);
            if (Status == ServerHello0Status.OK)
            {
                StatefulProofOfWorkType = (StatefulProofOfWorkType)reader.ReadByte();
                Snonce0 = PacketProcedures.DecodeByteArray256(reader);
            }
        }

    }
    enum ServerHello0Status
    {
        OK = 0, // continue to "stateful PoW" stage
        ErrorWeakStatelessProofOfWorkType = 3,
        ErrorBadStatelessProofOfWork = 4,
        ErrorBadStatelessProofOfWork_BadClock = 5,
        ErrorBadStatelessProofOfWork_BadSourceIp = 6,
        ErrorNeedToRegister = 10,
        ErrorTryAgainRightNowWithThisServer = 11, // non-unique stateless PoW
        ErrorTryLaterWithThisServer = 12, // temporary overload
        ErrorTryWithAnotherServer = 13, // with another server that is preconfigured at client side
        ErrorBadPacket = 14,
    }
    
    // ======================================================================= hello1 stage
    enum StatefulProofOfWorkType
    {
     //   none = 0,
        _2019_06 = 1, // client calculates StatefulProofOfWorkResponseData so that SHA256()
                      //  _captcha1 = 2, // ask user to enter captcha, send him url
    }
    /// <summary>
    /// third packet in the handshaking, sent from server to client
    /// </summary>
    class ClientHelloPacket1
    {
        byte Flags; // reserved
        public byte[] Snonce0; // must be reflected by client, from ServerHelloPacket0
        public byte[] StatefulProofOfWorkResponseData; // =cnonce1, must be reflected by server
        public const int StatefulProofOfWorkResponseDataSupportedSize = 32; // only 1 size now - 201906
        
        byte[] ClientSignature; // set if client is registered
        
        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            Encode(writer);
            return ms.ToArray();
        }
        /// <returns>offset to StatefulProofOfWorkResponseData</returns>
        public int Encode(BinaryWriter writer)
        {
            writer.Write((byte)CcpPacketType.ClientHelloPacket1);
            writer.Write(Flags);
            if (Snonce0.Length != ServerHelloPacket0.Snonce0SupportedSize) throw new CcpBadPacketException();
            PacketProcedures.EncodeByteArray256(writer, Snonce0);

            if (StatefulProofOfWorkResponseData.Length != StatefulProofOfWorkResponseDataSupportedSize) throw new CcpBadPacketException();
            PacketProcedures.EncodeByteArray256(writer, StatefulProofOfWorkResponseData);
            return 1 + 1 + 1 + Snonce0.Length + 1;
        }
        public readonly byte[] OriginalPacketPayload;
        public ClientHelloPacket1(BinaryReader reader, byte[] originalPacketPayload) // after first byte = packet type
        {
            OriginalPacketPayload = originalPacketPayload;
            Flags = reader.ReadByte();
            Snonce0 = PacketProcedures.DecodeByteArray256(reader);
            if (Snonce0.Length != ServerHelloPacket0.Snonce0SupportedSize) throw new CcpBadPacketException();
            StatefulProofOfWorkResponseData = PacketProcedures.DecodeByteArray256(reader);
        }
    }
    class ServerHelloPacket1
    {
        byte Flags;
        public ServerHello1Status Status;
        public byte[] Cnonce1; // set if status = OKready
        string[] Servers;
        byte[] ServerSignature;

        public StatefulProofOfWorkType StatefulProofOfWorkType { get; set; } // set if status=okready // pow for next ping request, against stateful DoS attacks
        public byte[] Snonce1 { get; set; } // = pow request data // set if status=okready

        public byte[] Encode()
        {
            PacketProcedures.CreateBinaryWriter(out var ms, out var writer);
            writer.Write((byte)CcpPacketType.ServerHelloPacket1);
            writer.Write(Flags);
            writer.Write((byte)Status);
            PacketProcedures.EncodeByteArray256(writer, Cnonce1);

            if (Status == ServerHello1Status.OKready)
            {
                writer.Write((byte)StatefulProofOfWorkType);
                PacketProcedures.EncodeByteArray256(writer, Snonce1);
            }
            return ms.ToArray();
        }
        public ServerHelloPacket1()
        {

        }
        public ServerHelloPacket1(BinaryReader reader) // after first byte = packet type
        {
            Flags = reader.ReadByte();
            Status = (ServerHello1Status)reader.ReadByte();
            Cnonce1 = PacketProcedures.DecodeByteArray256(reader);
            if (Status == ServerHello1Status.OKready)
            {
                StatefulProofOfWorkType = (StatefulProofOfWorkType)reader.ReadByte();
                Snonce1 = PacketProcedures.DecodeByteArray256(reader);
            }
        }
    }
    enum ServerHello1Status
    {
        OKready = 0, // continue to "ready" stage with current server
        OKredirect = 1, // continue to "hello0" with another servers
        ErrorBadStatefulProofOfWork = 1,
        ErrorGotoHello0 = 2, // server restarted and lost session token
    }

    /// <summary>
    /// is sent from client to server 
    /// over UDP, over DRP
    /// </summary>
    class ClientPingRequestPacket
    {
        byte Flags; //reserved
        byte[] ServerSessionToken { get; set; }
        byte[] EncryptedPayload { get; set; }
        byte[] PoWresponseData { get; set; }
        byte[] ClientSignature { get; set; }
    }
    class ServerPingResponsePacket
    {
        byte Flags; //reserved
        ServerPingResponseStatus Status;
        byte[] Payload { get; set; }
        StatefulProofOfWorkType PowType { get; set; }
        byte[] PoWrequestData { get; set; } // pow for next ping request, against stateful DoS attacks
        byte[] ServerSignature { get; set; }
    }
    enum ServerPingResponseStatus
    {
        OK = 0,
        ErrorGotoHello0withThisServer = 2, // server restarted and lost session token
        ErrorOverloadedTryConnectToAnotherServer = 3,
        ErrorOverloadedTryLaterWithThisServer = 4,
    }
}
