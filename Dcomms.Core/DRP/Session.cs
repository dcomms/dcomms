using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP
{
    class Session
    {
        public readonly SessionDescription LocalSessionDescription;
        public SessionDescription RemoteSessionDescription { get; set; }

        readonly byte[] LocalEcdhePrivateKey;
        readonly public byte[] LocalEcdhePublicKey;
        byte[] SharedEcdhSecret;

        readonly LocalDrpPeer _localDrpPeer;
        public Session(LocalDrpPeer localDrpPeer, SessionDescription localSessionDescription)
        {
            LocalSessionDescription = localSessionDescription;
            _localDrpPeer = localDrpPeer;
            _localDrpPeer.CryptoLibrary.GenerateEcdh25519Keypair(out LocalEcdhePrivateKey, out LocalEcdhePublicKey);
        }
    }
}
