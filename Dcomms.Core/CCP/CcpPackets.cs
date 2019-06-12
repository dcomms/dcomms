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
        byte[] StatelessProofOfWorkData;
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
            return ms.ToArray();
        }
        public ClientHelloPacket0(BinaryReader reader) // after first byte = packet type
        {
            Flags = reader.ReadUInt16();
            ClientHelloToken = PacketProcedures.DecodeByteArray256(reader);
            StatelessProofOfWorkType = (StatelessProofOfWorkType)reader.ReadByte();
        }
    }
    enum StatelessProofOfWorkType
    {
        none = 0,
        _2019_05 = 1, // sha256(result||client_public_ip||time_now) has N MSB set to zero;  data is: result||client_public_ip||time_now
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
            writer.Write((byte)StatefulProofOfWorkType);
            return ms.ToArray();
        }
        public ServerHelloPacket0(BinaryReader reader) // after first byte = packet type
        {
            Flags = reader.ReadUInt16();
            Status = (ServerHello0Status)reader.ReadByte();
            ClientHelloToken = PacketProcedures.DecodeByteArray256(reader);
            StatefulProofOfWorkType = (StatefulProofOfWorkType)reader.ReadByte();
        }

    }
    enum ServerHello0Status
    {
        OK = 0, // continue to "stateful PoW" stage
        ErrorWeakStatelessProofOfWorkType = 3,
        ErrorBadStatelessProofOfWork = 4,
        ErrorNeedToRegister = 5,
        ErrorTryLaterWithThisServer = 6, // temporary overload
        ErrorTryWithAnotherServer = 7, // with another server that is preconfigured at client side
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
