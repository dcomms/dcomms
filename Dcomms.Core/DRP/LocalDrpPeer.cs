using Dcomms.Cryptography;
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
    public partial class LocalDrpPeer: IDisposable
    {
        /// <summary>
        /// is used for:
        /// - PoW1
        /// - EpEndpoint vlidateion at responder
        /// - RequesterEndpoint validation at requester
        /// </summary>
        public IPAddress PublicIpApiProviderResponse;
       // LocalDrpPeerState State;
        readonly DrpPeerRegistrationConfiguration _registrationConfiguration;
        public DrpPeerRegistrationConfiguration RegistrationConfiguration => _registrationConfiguration;
        readonly IDrpRegisteredPeerApp _drpPeerApp;
        readonly DrpPeerEngine _engine;
        internal ICryptoLibrary CryptoLibrary => _engine.CryptoLibrary;
        public LocalDrpPeer(DrpPeerEngine engine, DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerApp drpPeerApp)
        {
            _engine = engine;
            _registrationConfiguration = registrationConfiguration;
            _drpPeerApp = drpPeerApp;
        }
        public List<ConnectionToNeighbor> ConnectedNeighbors = new List<ConnectionToNeighbor>();
       
        public void Dispose() // unregisters
        {

        }
        public override string ToString() => _registrationConfiguration.LocalPeerRegistrationPublicKey.ToString();
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
