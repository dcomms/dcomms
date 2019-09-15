using Dcomms.Cryptography;
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
        internal byte[] SharedDhSecret;
        internal void DeriveSharedDhSecret(ICryptoLibrary cryptoLibrary, byte[] remotePublicEcdheKey)
        {
            SharedDhSecret = cryptoLibrary.DeriveEcdh25519SharedSecret(LocalEcdhePrivateKey, remotePublicEcdheKey);
        }

        readonly LocalDrpPeer _localDrpPeer;
        public Session(LocalDrpPeer localDrpPeer)
        {
            _localDrpPeer = localDrpPeer;
            _localDrpPeer.CryptoLibrary.GenerateEcdh25519Keypair(out LocalEcdhePrivateKey, out LocalEcdhePublicKey);
        }
    }
}
