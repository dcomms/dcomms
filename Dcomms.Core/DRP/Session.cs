using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP
{
    public class Session
    {
        public SessionDescription LocalSessionDescription { get; set; }
        public SessionDescription RemoteSessionDescription { get; set; }

        readonly byte[] LocalEcdhePrivateKey;
        readonly public byte[] LocalEcdhePublicKey;
        internal byte[] SharedEcdhSecret;

        readonly LocalDrpPeer _localDrpPeer;
        public Session(LocalDrpPeer localDrpPeer)
        {
            _localDrpPeer = localDrpPeer;
            _localDrpPeer.CryptoLibrary.GenerateEcdh25519Keypair(out LocalEcdhePrivateKey, out LocalEcdhePublicKey);
        }
    }
}
