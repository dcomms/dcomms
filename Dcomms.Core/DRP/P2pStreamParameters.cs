﻿using Dcomms.Cryptography;
using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{

    /// <summary>
    /// is generated by remote peer; token of local peer at remote peer;
    /// put into every p2p packet,
    /// is needed  1) for faster lookup of remote peer by first 16 of 32 bits 2) to have multiple DRP peer reg IDs running at same UDP port
    /// is unique at remote (responder) peer; is used to identify local (sender) peer at remote peer (together with HMAC)
    /// </summary>
    public class RemotePeerToken32
    {
        public uint Token32;
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Token32);
        }
        public static RemotePeerToken32 Decode(BinaryReader reader)
        {
            var r = new RemotePeerToken32();
            r.Token32 = reader.ReadUInt32();
            return r;
        }
    }

    /// <summary>
    /// parameters to transmit DRP pings and proxied packets between registered neighbors:
    /// from local peer to remote peer (txParamaters)
    /// from remote peer to local peer (rxParamaters)
    /// is negotiated via REGISTER channel
    /// all fields are encrypted when transmitted over REGISTER channel, using single-block AES and shared ECDH key
    /// </summary>
    public class P2pStreamParameters
    {
        public RemotePeerToken32 RemotePeerToken32;
        public IPEndPoint RemoteEndpoint; // IP address + UDP port // where to send packets
     
        public static P2pStreamParameters DecryptAtRegisterRequester(byte[] localPrivateEcdhKey, RegisterSynPacket localRegisterSyn,
            RegisterSynAckPacket remoteRegisterSynAck, ICryptoLibrary cryptoLibrary)
        {
            if ((remoteRegisterSynAck.Flags & RegisterSynAckPacket.Flag_ipv6) != 0) throw new NotImplementedException();

            var r = new P2pStreamParameters();

            var sharedDhSecret = cryptoLibrary.DeriveEcdh25519SharedSecret(localPrivateEcdhKey, remoteRegisterSynAck.NeighborEcdhePublicKey.ecdh25519PublicKey);

            var ms = new MemoryStream();      
            using (var writer = new BinaryWriter(ms))
            {
                localRegisterSyn.GetCommonRequesterAndResponderFields(writer);
                remoteRegisterSynAck.GetCommonRequesterAndResponderFields(writer);
            }
            var iv = cryptoLibrary.GetHashSHA256(ms.ToArray());

            ms.Write(sharedDhSecret, 0, sharedDhSecret.Length);
            var aesKey = cryptoLibrary.GetHashSHA256(ms.ToArray()); // here SHA256 is used as KDF, together with common fields from packets, including both ECDH public keys and timestamp

            var toNeighborTxParametersDecrypted = new byte[remoteRegisterSynAck.ToNeighborTxParametersEncrypted.Length];
            cryptoLibrary.ProcessSingleAesBlock(false, aesKey, iv, remoteRegisterSynAck.ToNeighborTxParametersEncrypted, toNeighborTxParametersDecrypted);

            // parse toNeighborTxParametersDecrypted
            using (var reader = new BinaryReader(new MemoryStream(toNeighborTxParametersDecrypted)))
            {
                r.RemoteEndpoint = PacketProcedures.DecodeIPEndPointIpv4(reader);
                r.RemotePeerToken32 = RemotePeerToken32.Decode(reader);
                var magic32 = reader.ReadUInt32();
                if (magic32 != Magic32_ipv4) throw new BrokenCipherException();
            }
            
            return r;
        }
        const uint Magic32_ipv4 = 0x692A60C1;
        public static P2pStreamParameters CreateForRegisterResponder()
        {
            throw new NotImplementedException();
        }



        //public HMAC GetLocalSenderHmac(RegisterSynPacket registerSyn)
        //{
        //    throw new NotImplementedException();
        //}


    }
}
