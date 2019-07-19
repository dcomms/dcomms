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
    class RegisterSynPacket
    {
        // часть SYN
        byte[] RegPubA; // used to verify signature
        uint timestamp;
        byte powType; // 3 bits //todo: argon2 cpuPoW
        byte[] CpuPoWa; // =nonceA=messageID,    // sha256(RegPubA|timestamp|CpuPoWa) has byte[6]=7
        byte[] RegSignA; // часть SYN // is verified by RP,M,N
        byte HopsRemaining;// max 10

        byte[][] ExceptTheseNeighbors; // только для расширения соседей
    }

    class RegisterProxiedSynPacket
    {
        // часть SYN
        byte[] RegPubA; // used to verify signature
        uint timestamp;
        byte powType; // 3 bits
        byte[] CpuPoWa; // =nonceA=messageID,    // sha256(RegPubA|timestamp|CpuPoWa) has byte[6]=7
        byte[] RegSignA; // часть SYN // is verified by RP,M,N
        byte HopsRemaining;// decremented

        byte[] RegPubSender; // = RP,M,X  sender=тот, кто проксирует
        byte[] RegSignSender; //подпись последнего отправителя// весь пакет
    }

    /// <summary>
    /// ответ от RP к A идет по тем же hops
    /// узлы помнят обратный путь  по 
    /// </summary>
    class RegisterSynAckPacket
    {
        // not null only for N-X-M-RP-A  (status=connecting_ntoa)
        byte[] ResponderEndpoint_encryptedByRegPubA;
        
        DrpStatusCode StatusCode;

        // SYN part:
        byte[] RegPubA; // copied from request
        uint timestamp; // copied from request
        byte[] cpuPoWa; // copied from request //=messageId

        // ACK:
        byte[] RegPubN; // pub key of RP, Mm N
      //// ???     byte[] NonceN; // not needed: alrady enough of salt
        byte[] RegSignN; // весь пакет //=cpuPoWn        
    }

    /// <summary>
    /// A-RP
    /// пиры помнят путь по messageid=CpuPoWa
    /// </summary>
    class RegisterAckPacket
    {
        // syn part
        byte[] RegPubA;
        uint timestamp;
        byte[] CpuPoWa; // =nonceA=messageID, // sha256(RegPubA|timestamp|CpuPoWa) has byte[6]=7

        byte[] EndointA_encryptedByPubN;

        byte[] RegSignA; // is verified by RP,M,N
        byte HopsRemaining;
    }
    /// <summary>
    /// А получил пинг от N
    /// A-RP
    /// пиры помнят путь по messageid=CpuPoWa
    /// пиры финализуруют состояние, обновляют рейтинг (всех по цепочке)
    /// </summary>
    class RegisterConfirmedPacket
    {
        // syn part
        byte[] RegPubA;
        uint timestamp;
        byte[] CpuPoWa; // =nonceA=messageID, // sha256(RegPubA|timestamp|CpuPoWa) has byte[6]=7

        byte[] EndointA_encryptedByPubN;

        byte[] RegSignA; // is verified by RP,M,N
        byte HopsRemaining;
    }



    enum DrpStatusCode
    {
        proxied, // is sent to previous hop immediately when packet is proxied, to avoid retransmissions
        connecting,
        
        rejected, // no neighbors
        rejected_badtimestamp,
        rejected_maxhopsReached
    }
    class PingPacket
    {
        byte flags; // bit0 = "bad time"   // attack on neighbor: fake "bad time": max time sync per minute
        uint timestamp; // against replay: receiver checks timestamp
    //// Alexey: not needed  byte[] nonceA; // ?? with ed25519 not needed???  
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
