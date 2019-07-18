using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{
    /// <summary>
    /// is sent from A to RP, from RP to M, from M to N
    /// A = original requester
    /// </summary>
    class RegisterRequestPacket
    {
        IPEndPoint EndointA;
        byte[] RegPubA;
        uint timestamp;
        byte[] CpuPoWa; // =nonceA=messageID,
        byte[] RegSignA;
        byte HopsRemaining;
    }

    /// <summary>
    /// sent from neighbor N who agrees to set up connection back in same way; and directly to orginal requester IP
    /// response with statusCode=proxied is sent to previous hop immediately when packet is proxied, to avoid retransmissions
    /// </summary>
    class RegisterResponsePacket
    {
        byte[] ResponderEndpoint_encryptedByRegPubA;
        DrpStatusCode StatusCode;
        byte[] RegPubA; // copied from request
        uint timestamp; // copied from request
        byte[] cpuPoWa; // copied from request
        byte[] RegPubN;
        byte[] NonceN;
        byte[] RegSignN;//=cpuPoWn
    }
    enum DrpStatusCode
    {
        proxied,
        connected,
        rejected,
        rejected_badtimestamp,
        maxhopsReached
    }
    class PingPacket
    {
        uint timestamp;
        byte[] nonceA;
        byte[] regSignA;
    ////////////    byte[] cpuPoWa;//??????????
    }
    class InviteRequestPacket
    {
        byte[] DirectChannelEndointA_encryptedByRegPubB; // with nonce; can be decrypted only by B
        byte[] RegPubA;
        byte[] RegPubB;
        uint timestamp;
        byte[] RegSignA;
        byte[] CpuPoWa; // =nonceA=messageID
        byte hopsRemaining;
    }
    class InviteResponsePacket
    {
        DrpStatusCode StatusCode;
        byte[] DirectChannelEndointB_encryptedByRegPubA; // with nonce; can be decrypted only by A
        byte[] RegPubA;
        byte[] RegPubB;
        uint timestamp;
        byte[] RegSignB;
        byte[] CpuPoWa; // =nonceA=messageID
    }
}
