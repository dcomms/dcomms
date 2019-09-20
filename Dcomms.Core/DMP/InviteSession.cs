using Dcomms.Cryptography;
using Dcomms.DRP;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.DMP
{
    public class InviteSession
    {
        public InviteSessionDescription LocalSessionDescription { get; set; }
        public InviteSessionDescription RemoteSessionDescription { get; set; }

        readonly byte[] LocalEcdhePrivateKey;
        readonly public byte[] LocalEcdhePublicKey;
        internal byte[] SharedDhSecret;
        internal void DeriveSharedDhSecret(ICryptoLibrary cryptoLibrary, byte[] remotePublicEcdheKey)
        {
            SharedDhSecret = cryptoLibrary.DeriveEcdh25519SharedSecret(LocalEcdhePrivateKey, remotePublicEcdheKey);
        }

        readonly LocalDrpPeer _localDrpPeer;
        public InviteSession(LocalDrpPeer localDrpPeer)
        {
            _localDrpPeer = localDrpPeer;
            _localDrpPeer.CryptoLibrary.GenerateEcdh25519Keypair(out LocalEcdhePrivateKey, out LocalEcdhePublicKey);
        }



        public HMAC GetPingPongHMAC(Action<BinaryWriter> writeSignedFields)
        {
            throw new NotImplementedException();
        }
    }
}
