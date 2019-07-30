using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP
{
    public class RegistrationPublicKey
    {
        public byte[] ed25519publicKey;
    }
    public class RegistrationPrivateKey
    {
        public byte[] ed25519privateKey;
    }
    class SecretHeyForHmac
    {
        public byte[] secretkey; // is same at 2 neighbor peers
    }
    class HMAC
    {
        public byte[] hmac;
    }

}
