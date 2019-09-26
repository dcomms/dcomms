using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

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

        readonly DrpPeerRegistrationConfiguration _registrationConfiguration;
        public DrpPeerRegistrationConfiguration RegistrationConfiguration => _registrationConfiguration;
        readonly IDrpRegisteredPeerApp _drpPeerApp;
        internal readonly DrpPeerEngine Engine;
        internal ICryptoLibrary CryptoLibrary => Engine.CryptoLibrary;
        public LocalDrpPeer(DrpPeerEngine engine, DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerApp drpPeerApp)
        {
            Engine = engine;
            _registrationConfiguration = registrationConfiguration;
            _drpPeerApp = drpPeerApp;
        }
        public List<ConnectionToNeighbor> ConnectedNeighbors = new List<ConnectionToNeighbor>();
       
        public void Dispose() // unregisters
        {

        }
        public override string ToString() => _registrationConfiguration.LocalPeerRegistrationId.ToString();

        const double ConnectToNeighborPeriodToRetry = 20;
        DateTime? _currentConnectToNewNeighborOperationStartTimeUtc;
        async Task ConnectToNewNeighborIfNeededAsync(DateTime timeNowUtc)
        {
            if (ConnectedNeighbors.Count < _registrationConfiguration.NumberOfNeighborsToKeep)
            {
                if (_currentConnectToNewNeighborOperationStartTimeUtc == null || timeNowUtc > _currentConnectToNewNeighborOperationStartTimeUtc.Value.AddSeconds(ConnectToNeighborPeriodToRetry))
                {
                    _currentConnectToNewNeighborOperationStartTimeUtc = timeNowUtc;
                    try
                    {
                        //    todo extend neighbors via ep (10% probability)  or via existing neighbors --- increase mindistance, from 1
                   //     if (this.RegistrationConfiguration.EntryPeerEndpoints != null && (Engine.InsecureRandom.NextDouble() < 0.1 || ConnectedNeighbors.Count == 0))
                   //     {
                   //         var epEndpoint = this.RegistrationConfiguration.EntryPeerEndpoints[Engine.InsecureRandom.Next(this.RegistrationConfiguration.EntryPeerEndpoints.Length)];
                   //         await Engine.RegisterAsync(this, epEndpoint, 1);
                   //     }
                     //   else
                        {
                            if (ConnectedNeighbors.Count != 0)
                            {
                                var neighborToSendRegister = ConnectedNeighbors[Engine.InsecureRandom.Next(ConnectedNeighbors.Count)];
                                await neighborToSendRegister.RegisterAsync(1);
                            }
                        }
                    }
                    finally
                    {
                        _currentConnectToNewNeighborOperationStartTimeUtc = null;
                    }
                }
            }

        }


        internal void EngineThreadOnTimer100ms(DateTime timeNowUtc)
        {
            _ = ConnectToNewNeighborIfNeededAsync(timeNowUtc);            
        }

    }
}
