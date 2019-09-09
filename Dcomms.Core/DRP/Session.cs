using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP
{
    class Session
    {
        public SessionDescription LocalSessionDescription { get; set; }
        public SessionDescription RemoteSessionDescription { get; set; }

        byte[] LocalEcdhePrivateKey;
        byte[] LocalEcdhePublicKey;
        byte[] SharedEcdhSecret;
    }
}
