using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{
    /// <summary>
    /// "contact point" of local user in the regID space
    /// can be "registered" or "registering"
    /// </summary>
    public class LocalDrpPeer
    {
        public IPAddress LocalPublicIpAddressForRegistration;
       // LocalDrpPeerState State;
        readonly DrpPeerRegistrationConfiguration _registrationConfiguration;
        public DrpPeerRegistrationConfiguration RegistrationConfiguration => _registrationConfiguration;
        readonly IDrpRegisteredPeerUser _user;
        readonly DrpPeerEngine _engine;
        public LocalDrpPeer(DrpPeerEngine engine, DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerUser user)
        {
            _engine = engine;
            _registrationConfiguration = registrationConfiguration;
            _user = user;
        }
        public List<ConnectedDrpPeer> ConnectedPeers = new List<ConnectedDrpPeer>(); // neighbors
        /// <summary>
        /// main routing procedure
        /// selects next peer (hop) to proxy packet
        /// returns null in case of flood
        /// </summary>
        ConnectedDrpPeer TryRouteRequest(RegistrationPublicKey targetRegistrationPublicKey)
        {
            // enumerate conn. peers
            //   skip flooded tx connections (where tx rate is exceeded)
            //   get distance = xor;   combined with ping RTT and rating    (based on RDR)
            //   
            throw new NotImplementedException();
        }
        public void Dispose() // unregisters
        {

        }
    }
    //enum LocalDrpPeerState
    //{
    //    requestingPublicIp,
    //    pow,
    //    registerSynSent,
    //    pingEstablished,
    //    minNeighborsCountAchieved,
    //    achievedGoodRatingForNeighbors // ready to send requests
    //}
}
