using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP.Packets
{

    /// <summary>
    /// is sent from A to N with encrypted IP address of A
    /// A->RP->M->N
    /// пиры помнят путь по RequestID  пиры уже авторизовали друг друга на этом этапе
    /// </summary>
    class RegisterAckPacket
    {
        RemotePeerToken32 SenderToken32;
        byte ReservedFlagsMustBeZero;
        RegistrationPublicKey RequesterPublicKey_RequestID;
        P2pStreamParameters ToRequesterTxParaemters; // encrypted // IP address of A + UDP port + salt // initial IP address of A comes from RP  // possible attacks by RP???
        byte[] RequesterSignature; // is verified by N; MAY be verified by  RP, N

        HMAC SenderHMAC; // is NULL for A->RP
    }
}
