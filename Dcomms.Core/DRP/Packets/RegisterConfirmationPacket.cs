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
    public class RegisterConfirmationPacket
    {
        public byte ReservedFlagsMustBeZero;
        public RegistrationPublicKey RequesterPublicKey_RequestID;
        public RegistrationSignature NeighborP2pConnectionSetupSignature; // comes from pingResponse packet from neighbor //  is verified by N, RP,M  before updating rating
        public RegistrationSignature RequesterSignature; // is verified by N, RP,M  before updating rating // {includes Magic}

        public HMAC SenderHMAC; // is NULL for A->RP
    }

}
