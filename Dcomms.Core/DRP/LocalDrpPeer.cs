using Dcomms.Cryptography;
using Dcomms.Vision;
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
    public partial class LocalDrpPeer: IDisposable, IVisibleModule
    {
        /// <summary>
        /// is used for:
        /// - PoW1
        /// - EpEndpoint vlidateion at responder
        /// - RequesterEndpoint validation at requester
        /// </summary>
        public IPAddress PublicIpApiProviderResponse;

        readonly LocalDrpPeerConfiguration _configuration;
        public LocalDrpPeerConfiguration Configuration => _configuration;
        readonly IDrpRegisteredPeerApp _drpPeerApp;
        internal readonly DrpPeerEngine Engine;
        internal ICryptoLibrary CryptoLibrary => Engine.CryptoLibrary;

        string IVisibleModule.Status => $"connected neighbors: {ConnectedNeighbors.Count}/{_configuration.NumberOfNeighborsToKeep}. {CurrentRegistrationOperationsCount} pending reg.";

        public LocalDrpPeer(DrpPeerEngine engine, LocalDrpPeerConfiguration configuration, IDrpRegisteredPeerApp drpPeerApp)
        {
            Engine = engine;
            _configuration = configuration;
            _drpPeerApp = drpPeerApp;
            engine.Configuration.VisionChannel?.RegisterVisibleModule(engine.Configuration.VisionChannelSourceId, this.ToString(), this);
        }
        public List<ConnectionToNeighbor> ConnectedNeighbors = new List<ConnectionToNeighbor>();
        public int CurrentRegistrationOperationsCount;


        public void Dispose() // unregisters
        {

        }
        public override string ToString() => $"localDrpPeer{_configuration.LocalPeerRegistrationId}";

        const double ConnectToNeighborPeriodToRetry = 20;
        DateTime? _latestConnectToNewNeighborOperationStartTimeUtc;
        async Task ConnectToNewNeighborIfNeededAsync(DateTime timeNowUtc)
        {
            if (ConnectedNeighbors.Count < _configuration.NumberOfNeighborsToKeep)
            {
                if (CurrentRegistrationOperationsCount == 0 || timeNowUtc > _latestConnectToNewNeighborOperationStartTimeUtc.Value.AddSeconds(ConnectToNeighborPeriodToRetry))
                {
                    _latestConnectToNewNeighborOperationStartTimeUtc = timeNowUtc;
                   
                    //    extend neighbors via ep (10% probability)  or via existing neighbors --- increase mindistance, from 1
                    if (this.Configuration.EntryPeerEndpoints != null && (Engine.InsecureRandom.NextDouble() < 0.1 || ConnectedNeighbors.Count == 0))
                    {
                        var epEndpoint = this.Configuration.EntryPeerEndpoints[Engine.InsecureRandom.Next(this.Configuration.EntryPeerEndpoints.Length)];
                        Engine.WriteToLog_inv_requesterSide_higherLevelDetail($"extending neighborhood via EP {epEndpoint} ({ConnectedNeighbors.Count} connected neighbors now)");
                        await Engine.RegisterAsync(this, epEndpoint, 1);
                    }
                    else
                    {
                        if (ConnectedNeighbors.Count != 0)
                        {
                            var neighborToSendRegister = ConnectedNeighbors[Engine.InsecureRandom.Next(ConnectedNeighbors.Count)];
                            Engine.WriteToLog_inv_requesterSide_higherLevelDetail($"extending neighborhood via neighbor {neighborToSendRegister} ({ConnectedNeighbors.Count} connected neighbors now)");
                            await neighborToSendRegister.RegisterAsync(1);
                        }
                    }                   
                }
            }
        }
        
        internal void EngineThreadOnTimer100ms(DateTime timeNowUtc)
        {
            _ = ConnectToNewNeighborIfNeededAsync(timeNowUtc);            
        }
    }

    /// <summary>
    /// configuration of locally hosted DRP peer
    /// optionally contains config for registration
    /// </summary>
    public class LocalDrpPeerConfiguration
    {
        public IPEndPoint[] EntryPeerEndpoints; // in case when local peer IP = entry peer IP, it is skipped
        public RegistrationId LocalPeerRegistrationId { get; private set; }
        public RegistrationPrivateKey LocalPeerRegistrationPrivateKey { get; private set; }
        public int? NumberOfNeighborsToKeep;
        public static LocalDrpPeerConfiguration CreateWithNewKeypair(ICryptoLibrary cryptoLibrary)
        {;
            var privatekey = new RegistrationPrivateKey { ed25519privateKey = cryptoLibrary.GeneratePrivateKeyEd25519() };
            return new LocalDrpPeerConfiguration
            {
                LocalPeerRegistrationPrivateKey = privatekey,
                LocalPeerRegistrationId = new RegistrationId(cryptoLibrary.GetPublicKeyEd25519(privatekey.ed25519privateKey))
            };
        }
    }
}
