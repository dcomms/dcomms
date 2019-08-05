using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP.Packets
{

    /// <summary>
    /// is sent by A when it receives signed ping from N
    /// A->RP->M
    /// пиры уже авторизовали друг друга на этом этапе
    /// пиры финализуруют состояние, обновляют рейтинг (всех по цепочке)
    /// </summary>
    class RegisterConfirmedPacket
    {
        byte ReservedFlagsMustBeZero;
        RegistrationPublicKey RequesterPublicKey_RequestID;
        byte succeeded; // 1 bit, to make the signature different from initial "SYN" part
        byte[] RequesterSignature; // is verified by N, RP,M  before updating rating

        //todo add some signed data from N

        HMAC SenderHMAC; // is NULL for A->RP
    }

}
