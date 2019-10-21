using Dcomms.Cryptography;
using Dcomms.DRP.Packets;
using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    /// <summary>
    /// "contact point" of local user in the regID space
    /// can be "registered" or "registering"
    /// </summary>
    public partial class LocalDrpPeer: IDisposable, IVisibleModule, IVisiblePeer
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

        double[] _cachedLocalPeerRegistrationIdVectorValues;
        public double[] LocalPeerRegistrationIdVectorValues
        {
            get
            {
                if (_cachedLocalPeerRegistrationIdVectorValues == null)
                {
                    _cachedLocalPeerRegistrationIdVectorValues = RegistrationIdDistance.GetVectorValues(CryptoLibrary, Configuration.LocalPeerRegistrationId);
                }
                return _cachedLocalPeerRegistrationIdVectorValues;
            }
        }

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
        public ushort ConnectedNeighborsBusySectorIds
        {
            get
            {
                ushort r = 0;
                foreach (var n in ConnectedNeighbors)
                    r |= n.SectorIndexFlagsMask;
                return r;
            }
        }

        float[] IVisiblePeer.VectorValues => RegistrationIdDistance.GetVectorValues(CryptoLibrary, _configuration.LocalPeerRegistrationId).Select(x => (float)x).ToArray();
        bool IVisiblePeer.Highlighted => false;
        IEnumerable<IVisiblePeer> IVisiblePeer.NeighborPeers => ConnectedNeighbors;

        public IEnumerable<ConnectionToNeighbor> GetConnectedNeighborsForRouting(ConnectionToNeighbor sourceNeighborNullable,
            HashSet<ConnectionToNeighbor> alreadyTriedProxyingToDestinationPeersNullable,
            RegisterRequestPacket req)
        {
            foreach (var connectedPeer in ConnectedNeighbors.Where(x => x.PingReceived == true))
            {
                if (sourceNeighborNullable != null && connectedPeer == sourceNeighborNullable)
                {
                    Engine.WriteToLog_routing_detail($"skipping routing back to source peer {connectedPeer.RemoteRegistrationId}");
                    continue;
                }
                if (alreadyTriedProxyingToDestinationPeersNullable != null && alreadyTriedProxyingToDestinationPeersNullable.Contains(connectedPeer))
                {
                    Engine.WriteToLog_routing_detail($"skipping routing to previously tried peer {connectedPeer.RemoteRegistrationId}");
                    continue;
                }

                if (req.RequesterRegistrationId.Ed25519publicKey.Equals(connectedPeer.RemoteRegistrationId))
                {
                    Engine.WriteToLog_routing_detail($"skipping routing to peer with same regID {connectedPeer.RemoteRegistrationId}");
                    continue;
                }

                yield return connectedPeer;
            }
        }


        public void AddToConnectedNeighbors(ConnectionToNeighbor newConnectedNeighbor)
        {
            newConnectedNeighbor.OnP2pInitialized();
            ConnectedNeighbors.Add(newConnectedNeighbor);

            Engine.WriteToLog_p2p_higherLevelDetail(newConnectedNeighbor, $"added new connection to list of neighbors: {newConnectedNeighbor.RemoteRegistrationId} to {newConnectedNeighbor.RemoteEndpoint}");
        }
        public int CurrentRegistrationOperationsCount;
        
        public void Dispose() // unregisters
        {

        }
        public override string ToString() => $"localDrpPeer{_configuration.LocalPeerRegistrationId}";
               
        DateTime? _latestConnectToNewNeighborOperationStartTimeUtc;

        async Task ConnectToNewNeighborIfNeededAsync(DateTime timeNowUtc)
        {
            if (ConnectedNeighbors.Count < _configuration.NumberOfNeighborsToKeep)
            {
                if ((CurrentRegistrationOperationsCount == 0) ||
                    timeNowUtc > _latestConnectToNewNeighborOperationStartTimeUtc + TimeSpan.FromSeconds(Engine.Configuration.NeighborhoodExtensionMaxRetryIntervalS))
                {
                    if (_latestConnectToNewNeighborOperationStartTimeUtc.HasValue)
                    {
                        if ((timeNowUtc - _latestConnectToNewNeighborOperationStartTimeUtc.Value).TotalSeconds < Engine.Configuration.NeighborhoodExtensionMinIntervalS)
                            return;
                    }

                    _latestConnectToNewNeighborOperationStartTimeUtc = timeNowUtc;

                    try
                    {  
                        //    extend neighbors via ep (3% probability)  or via existing neighbors --- increase mindistance, from 1
                        if (this.Configuration.EntryPeerEndpoints != null && (Engine.InsecureRandom.NextDouble() < 0.03 || ConnectedNeighbors.Count == 0))
                        {
                            var epEndpoint = this.Configuration.EntryPeerEndpoints[Engine.InsecureRandom.Next(this.Configuration.EntryPeerEndpoints.Length)];
                            Engine.WriteToLog_reg_requesterSide_higherLevelDetail($"extending neighborhood via EP {epEndpoint} ({ConnectedNeighbors.Count} connected neighbors now)");
                            await Engine.RegisterAsync(this, epEndpoint, 0, 20);
                        }
                        else
                        {
                            if (ConnectedNeighbors.Count != 0)
                            {
                                var neighborToSendRegister = ConnectedNeighbors[Engine.InsecureRandom.Next(ConnectedNeighbors.Count)];
                                Engine.WriteToLog_reg_requesterSide_higherLevelDetail($"extending neighborhood via neighbor {neighborToSendRegister} ({ConnectedNeighbors.Count} connected neighbors now)");
                                await neighborToSendRegister.RegisterAsync(0, ConnectedNeighborsBusySectorIds, 20, 10);
                            }
                        }
                    }
                    //catch (DrpResponderRejectedMaxHopsReachedException exc)
                    //{
                    //    Engine.WriteToLog_reg_requesterSide_lightPain($"failed to extend neighbors for {this}: {exc}.    adjusting  numberOfHops...");

                    //    if (_numberOfHopsToExtendNeighbors < 50)
                    //        _numberOfHopsToExtendNeighbors = (byte)(_numberOfHopsToExtendNeighbors + 5);
                    //}
                    catch (DrpResponderRejectedP2pNetworkServiceUnavailableException exc)
                    {
                        Engine.WriteToLog_reg_requesterSide_lightPain($"failed to extend neighbors for {this}: {exc}");
                    }
                    catch (Exception exc)
                    {
                        Engine.WriteToLog_reg_requesterSide_mediumPain($"failed to extend neighbors for {this}: {exc}");
                    }
                }
            }
        }
        
        internal void EngineThreadOnTimer100ms(DateTime timeNowUtc)
        {
            _ = ConnectToNewNeighborIfNeededAsync(timeNowUtc);            
        }

        string IVisiblePeer.GetDistanceString(IVisiblePeer toThisPeer)
        {
            throw new NotImplementedException();
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
        {
            var privatekey = new RegistrationPrivateKey { ed25519privateKey = cryptoLibrary.GeneratePrivateKeyEd25519() };
            return new LocalDrpPeerConfiguration
            {
                LocalPeerRegistrationPrivateKey = privatekey,
                LocalPeerRegistrationId = new RegistrationId(cryptoLibrary.GetPublicKeyEd25519(privatekey.ed25519privateKey))
            };
        }
    }
}
