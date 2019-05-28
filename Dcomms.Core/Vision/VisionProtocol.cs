using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.Vision
{
    /// <summary>
    /// first packet sent from client to server
    /// </summary>
    class ClientHelloPacket0
    {
        StatelessProofOfWorkSuite StatelessProofOfWorkSuite;
        byte[] StatelessProofOfWorkData; // max 256 bytes
        byte[] ClientToken;
    }
    enum StatelessProofOfWorkSuite
    {
        none = 0,
        _2019_05 = 1, // sha256(result||client_ip||time_now) has N MSB set to zero
    }

    /// <summary>
    /// second packet, sent from server to client
    /// after sending this packet, vision server creates a state linked to client IP:port, for 10 secs, creates a task for "stateful" proof of work
    /// </summary>
    class ServerHelloPacket0
    {
        byte[] ClientToken; // to avoid server spoofing
        ServerHello0Status Status;
        StatefulProofOfWorkSuite StatefulProofOfWorkSuite;
        byte[] StatefulProofOfWorkRequestData; // max 256 bytes
    }
    enum ServerHello0Status
    {
        OK = 0, // continue to "stateful PoW" stage
        RedirectToAnotherServer = 1, // ???????????xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx server spoofed attacks
        ErrorWeakStatelessProofOfWorkSuite = 2,
        ErrorBadStatelessProofOfWork = 3,
    }
    enum StatefulProofOfWorkSuite
    {
        none = 0,
        _2019_05 = 1, // send client token in StatefulProofOfWorkRequestData, and expect same value back in Hello1
    }

    class ServerHelloPacket1
    {
        StatefulProofOfWorkSuite StatefulProofOfWorkSuite;
        byte[] StatefulProofOfWorkResponseData; // max 256 bytes
    }



    class ClientPingPacket
    {
        byte[] ClientToken { get; set; }
    }
}
