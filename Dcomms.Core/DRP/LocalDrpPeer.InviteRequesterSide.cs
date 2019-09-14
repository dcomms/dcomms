using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    partial class LocalDrpPeer
    {

        public async Task<Session> SendInviteAsync(RegistrationPublicKey responderPublicKey, SessionDescription localSessionDescription)
        {
            var session = new Session(this, localSessionDescription);


            var syn = new InviteSynPacket
            {
                NumberOfHopsRemaining = 10,
                RequesterEcdhePublicKey = new EcdhPublicKey(session.LocalEcdhePublicKey),
                RequesterPublicKey = this.RegistrationConfiguration.LocalPeerRegistrationPublicKey,
                ResponderPublicKey = responderPublicKey,
                Timestamp32S = _engine.Timestamp32S,
            };
            // find best connected peer to send the request
            ConnectionToNeighbor connectionToNeighbor = _engine.RouteSynInviteAtRequester(this, syn);

            var synUdpData = syn.Encode_SetP2pFields(connectionToNeighbor);

            await connectionToNeighbor.SendUdpRequestAsync_Retransmit_WaitForNHACK(synUdpData, syn.NhaSeq16);
      
            // wait for synack

            // decode and verify SD

            // send ack

            return session;
        }
    }
}
