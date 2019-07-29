﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP
{
    public class RegistrationPublicKey
    {
        byte[] ed25519publicKey;
    }
    public class RegistrationPrivateKey
    {
        byte[] ed25519privateKey;
    }
    class SecretHeyForHmac
    {
        byte[] secretkey; // is same at 2 neighbor peers
    }
    class HMAC
    {
        byte[] hmac;
    }

}
