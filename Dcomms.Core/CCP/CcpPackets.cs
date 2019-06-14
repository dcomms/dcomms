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
    
    // ======================================================================= hello0 stage

    /// <summary>
    /// very first packet sent from client to server
    /// </summary>
    class ClientHelloPacket0
    {
        public ushort Flags; // reserved // cipher suites
        public byte[] ClientHelloToken; // acts as cnonce and Diffie-Hellman exchange data  // to avoid conflicts between instances // to avoid server spoofing, source for server's signature
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
            writer.Write((byte)CcpPacketType.ClientHelloPacket0);
            writer.Write(Flags);
            PacketProcedures.EncodeByteArray256(writer, ClientHelloToken);
            writer.Write((byte)StatelessProofOfWorkType);
            PacketProcedures.EncodeByteArray256(writer, StatelessProofOfWorkData);
            return ms.ToArray();
        }
        public readonly byte[] OriginalPacketPayload;
        public ClientHelloPacket0(BinaryReader reader, byte[] originalPacketPayload) // after first byte = packet type
        {
            OriginalPacketPayload = originalPacketPayload;
            Flags = reader.ReadUInt16();
            ClientHelloToken = PacketProcedures.DecodeByteArray256(reader);
            StatelessProofOfWorkType = (StatelessProofOfWorkType)reader.ReadByte();
            StatelessProofOfWorkData = PacketProcedures.DecodeByteArray256(reader);
        }
    }
    enum StatelessProofOfWorkType
    {
        none = 0,
        _2019_06 = 1, // sha256(pow_data||rest_packet_data||time_now) has bytes 4..6 set to 7;  al
    }

    /// <summary> 
    /// second packet, sent from server to client
    /// after sending this packet, vision server creates a state linked to client IP:port, for 10 secs, creates a task for "stateful" proof of work
    /// </summary>
    class ServerHelloPacket0
    {
        public ushort Flags; //reserved
        public ServerHello0Status Status;
        public byte[] ClientHelloToken; // must be reflected by server
        // following fields are set if status = OK 
        public StatefulProofOfWorkType StatefulProofOfWorkType;
        public byte[] StatefulProofOfWorkRequestData; 
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
            PacketProcedures.EncodeByteArray256(writer, ClientHelloToken);

            if (Status == ServerHello0Status.OK)
            {
                writer.Write((byte)StatefulProofOfWorkType);
                PacketProcedures.EncodeByteArray256(writer, StatefulProofOfWorkRequestData);
            }
            return ms.ToArray();
        }
        public ServerHelloPacket0(BinaryReader reader) // after first byte = packet type
        {
            Flags = reader.ReadUInt16();
            Status = (ServerHello0Status)reader.ReadByte();
            ClientHelloToken = PacketProcedures.DecodeByteArray256(reader);
            if (Status == ServerHello0Status.OK)
            {
                StatefulProofOfWorkType = (StatefulProofOfWorkType)reader.ReadByte();
                StatefulProofOfWorkRequestData = PacketProcedures.DecodeByteArray256(reader);
            }
        }

    }
    enum ServerHello0Status
    {
        OK = 0, // continue to "stateful PoW" stage
        ErrorWeakStatelessProofOfWorkType = 3,
        ErrorBadStatelessProofOfWork = 4,
        ErrorBadStatelessProofOfWork_BadClock = 5,
        ErrorNeedToRegister = 10,
        ErrorTryAgainRightNowWithThisServer = 11, // non-unique stateless PoW
        ErrorTryLaterWithThisServer = 12, // temporary overload
        ErrorTryWithAnotherServer = 13, // with another server that is preconfigured at client side
        ErrorBadPacket = 14,
    }
    
    // ======================================================================= hello1 stage
    enum StatefulProofOfWorkType
    {
        none = 0,
        _2019_05 = 1, // send client token in StatefulProofOfWorkRequestData, and expect same value back in Hello1
        _captcha1 = 1, // ask user to enter captcha, send him url
    }
    class ClientHelloPacket1
    {
        byte Flags; // reserved
        byte[] ServerHelloToken; 
        byte[] StatefulProofOfWorkResponseData;
        byte[] ClientSignature; // set if client is registered
    }
    class ServerHelloPacket1
    {
        ServerHello1Status Status;
        byte[] ClientHelloToken; // set if status = OKready
        string[] Servers;
        byte[] ServerSignature;
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
        byte[] Signature { get; set; }
    }
    class ServerPingResponsePacket
    {
        byte Flags; //reserved
        ServerPingResponseStatus Status;
        byte[] Payload { get; set; }
    }
    enum ServerPingResponseStatus
    {
        OK = 0,
        ErrorGotoHello0 = 2, // server restarted and lost session token
    }
}
