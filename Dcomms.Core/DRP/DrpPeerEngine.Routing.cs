using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dcomms.DRP
{
    partial class DrpPeerEngine
    {

        /// <summary>
        /// main routing procedure for register REQ requests
        /// </summary>
        /// <param name="receivedAtLocalDrpPeerNullable">
        /// is set when routing REQ packets that are received via P2P connection from neighbor to the LocalDrpPeer
        /// </param>
        internal bool RouteRegistrationRequest(LocalDrpPeer receivedAtLocalDrpPeerNullable, ConnectionToNeighbor sourceNeighborNullable,
            HashSet<ConnectionToNeighbor> alreadyTriedProxyingToDestinationPeersNullable,
            RegisterRequestPacket req, out ConnectionToNeighbor proxyToDestinationPeer, out LocalDrpPeer acceptAt)
        {
            proxyToDestinationPeer = null;
            acceptAt = null;

            var localPeerForLogging = receivedAtLocalDrpPeerNullable ?? sourceNeighborNullable?.LocalDrpPeer ?? LocalPeers.Values.FirstOrDefault();

            if (req.NumberOfHopsRemaining <= 1)
            {
                var possibleAcceptAt = receivedAtLocalDrpPeerNullable ?? sourceNeighborNullable?.LocalDrpPeer;
                if (possibleAcceptAt == null)
                    possibleAcceptAt = LocalPeers.Values.Where(x => x.Configuration.LocalPeerRegistrationId.Equals(req.RequesterRegistrationId) == false).FirstOrDefault();

                if (possibleAcceptAt != null)
                {
                    if (!possibleAcceptAt.ConnectedNeighbors.Any(x => x.RemoteRegistrationId.Equals(req.RequesterRegistrationId)))
                    {
                        WriteToLog_routing_higherLevelDetail($"accepting registration request {req.RequesterRegistrationId} at local DRP peer {acceptAt}: low number of hops remaining", req, localPeerForLogging);
                        acceptAt = possibleAcceptAt;
                        return true;
                    }
                    else
                        WriteToLog_routing_higherLevelDetail($"not accepting registration at local DRP peer" +
                            $" {acceptAt} when low number of hops remaining: already connected", req, localPeerForLogging);

                }
            }

            if (req.RandomModeAtThisHop)
            { // random mode
                var itemsForRouting = new List<object>();
                if (sourceNeighborNullable == null)
                {
                    foreach (var localDrpPeer in LocalPeers.Values)
                    {
                        itemsForRouting.AddRange(localDrpPeer.GetConnectedNeighborsForRouting(sourceNeighborNullable, alreadyTriedProxyingToDestinationPeersNullable, req));
                        if (!localDrpPeer.Configuration.LocalPeerRegistrationId.Equals(req.RequesterRegistrationId))
                            itemsForRouting.Add(localDrpPeer);
                    }
                }
                else
                {
                    itemsForRouting.AddRange(sourceNeighborNullable.LocalDrpPeer.GetConnectedNeighborsForRouting(sourceNeighborNullable, alreadyTriedProxyingToDestinationPeersNullable, req));
                    if (!sourceNeighborNullable.LocalDrpPeer.Configuration.LocalPeerRegistrationId.Equals(req.RequesterRegistrationId))
                        itemsForRouting.Add(sourceNeighborNullable.LocalDrpPeer);
                }

                if (itemsForRouting.Count != 0)
                {
                    var itemForRouting = itemsForRouting[_insecureRandom.Next(itemsForRouting.Count - 1)];
                    WriteToLog_routing_higherLevelDetail($"routing registration in random mode to one of {itemsForRouting.Count} destinations: {proxyToDestinationPeer}", req, localPeerForLogging);
                    if (itemForRouting is ConnectionToNeighbor) proxyToDestinationPeer = (ConnectionToNeighbor)itemForRouting;
                    else acceptAt = (LocalDrpPeer)itemForRouting;
                }
                else WriteToLog_routing_needsAttention($"can not route registration in random mode: no destinations available including local peers", req, localPeerForLogging);
            }
            else
            {
                double? maxP2pConnectionValue = null;
                if (receivedAtLocalDrpPeerNullable != null)
                {
                    RouteRegistrationRequest_LocalDrpPeerIteration(receivedAtLocalDrpPeerNullable, sourceNeighborNullable, alreadyTriedProxyingToDestinationPeersNullable, req, ref maxP2pConnectionValue, ref proxyToDestinationPeer, ref acceptAt);
                }
                else
                {
                    foreach (var localDrpPeer in LocalPeers.Values)
                        RouteRegistrationRequest_LocalDrpPeerIteration(localDrpPeer, sourceNeighborNullable, alreadyTriedProxyingToDestinationPeersNullable, req, ref maxP2pConnectionValue, ref proxyToDestinationPeer, ref acceptAt);
                }
            }

            if (proxyToDestinationPeer != null)
            {
                WriteToLog_routing_higherLevelDetail($"proxying registration to {proxyToDestinationPeer}", req, localPeerForLogging);
                return true;
            }
            else if (acceptAt != null)
            {
                WriteToLog_routing_higherLevelDetail($"accepting registration at local DRP peer {acceptAt}", req, localPeerForLogging);
                return true;
            }
            else
            {
                WriteToLog_routing_needsAttention($"no route found for REGISTER request to {req.RequesterRegistrationId}", req, localPeerForLogging);
                return false;
            }
        }




        void RouteRegistrationRequest_LocalDrpPeerIteration(LocalDrpPeer localDrpPeer, ConnectionToNeighbor sourceNeighborNullable,
            HashSet<ConnectionToNeighbor> alreadyTriedProxyingToDestinationPeersNullable,
            RegisterRequestPacket req,
            ref double? maxP2pConnectionValue,
            ref ConnectionToNeighbor proxyToDestinationPeer, ref LocalDrpPeer acceptAt)
        {
            var connectedNeighborsForRouting = localDrpPeer.GetConnectedNeighborsForRouting(sourceNeighborNullable, alreadyTriedProxyingToDestinationPeersNullable, req).ToList();

            int connectedNeighborsCountThatMatchMinDistance = 0;
            foreach (var neighbor in connectedNeighborsForRouting)
            {
                var distanceToNeighbor = req.RequesterRegistrationId.GetDistanceTo(_cryptoLibrary, neighbor.RemoteRegistrationId, NumberOfDimensions);
                WriteToLog_routing_detail($"distanceToNeighbor={distanceToNeighbor} from REGISTER REQ {req.RequesterRegistrationId} to {neighbor} (req.min={req.MinimalDistanceToNeighbor})", req, localDrpPeer);
                if (req.MinimalDistanceToNeighbor != 0)
                {
                    if (distanceToNeighbor.IsLessThan(req.MinimalDistanceToNeighbor))
                    {
                        // skip: this is too close than requested
                        WriteToLog_routing_detail($"skipping connection to {neighbor}: distance={distanceToNeighbor} is less than requested={req.MinimalDistanceToNeighbor}", req, localDrpPeer);
                        continue;
                    }
                }
                var p2pConnectionValue_withNeighbor = P2pConnectionValueCalculator.GetMutualP2pConnectionValue(CryptoLibrary, req.RequesterRegistrationId, req.RequesterNeighborsBusySectorIds,
                    neighbor.RemoteRegistrationId, neighbor.RemoteNeighborsBusySectorIds ?? 0, NumberOfDimensions);

                WriteToLog_routing_detail($"p2pConnectionValue_withNeighbor={p2pConnectionValue_withNeighbor} from REGISTER REQ {req.RequesterRegistrationId} to {neighbor}", req, localDrpPeer);

                connectedNeighborsCountThatMatchMinDistance++;
                if (maxP2pConnectionValue == null || maxP2pConnectionValue < p2pConnectionValue_withNeighbor)
                {
                    maxP2pConnectionValue = p2pConnectionValue_withNeighbor;
                    proxyToDestinationPeer = neighbor;
                    acceptAt = null;
                }
            }

            if (connectedNeighborsCountThatMatchMinDistance == 0 && connectedNeighborsForRouting.Count != 0)
            {
                // special case: we are inside the "minDistance" hypersphere: move away from the requester, proxy to most distant neighbor
                WriteToLog_routing_detail($"special case: move away from the requester, proxy to most distant neighbor", req, localDrpPeer);

                RegistrationIdDistance maxDistance = null;
                foreach (var connectedPeer in connectedNeighborsForRouting)
                {         
                    var distanceToConnectedPeer = req.RequesterRegistrationId.GetDistanceTo(_cryptoLibrary, connectedPeer.RemoteRegistrationId, NumberOfDimensions);
                    WriteToLog_routing_detail($"distanceToConnectedPeer={distanceToConnectedPeer} from REGISTER REQ {req.RequesterRegistrationId} to {connectedPeer.RemoteRegistrationId} (req.min={req.MinimalDistanceToNeighbor})", req, localDrpPeer);
                   
                    if (maxDistance == null || distanceToConnectedPeer.IsGreaterThan(maxDistance))
                    {
                        maxDistance = distanceToConnectedPeer;
                        proxyToDestinationPeer = connectedPeer;
                        acceptAt = null;
                    }
                }
            }

            // dont connect to local peer if already connected
            if (localDrpPeer.Configuration.LocalPeerRegistrationId.Equals(req.RequesterRegistrationId) == true)
            {
                WriteToLog_routing_detail($"not accepting request at local peer: it has same regID {req.RequesterRegistrationId}", req, localDrpPeer);
            }
            else if (localDrpPeer.ConnectedNeighbors.Any(x => x.RemoteRegistrationId.Equals(req.RequesterRegistrationId)) == true)
            {
                WriteToLog_routing_detail($"not accepting request at local peer: already connected to {req.RequesterRegistrationId}", req, localDrpPeer);
            }
            else
            {
                var p2pConnectionValue_withLocalPeer = P2pConnectionValueCalculator.GetMutualP2pConnectionValue(CryptoLibrary, req.RequesterRegistrationId, req.RequesterNeighborsBusySectorIds, localDrpPeer.Configuration.LocalPeerRegistrationId, localDrpPeer.ConnectedNeighborsBusySectorIds, NumberOfDimensions);
                WriteToLog_routing_detail($"p2pConnectionValue_withLocalPeer={p2pConnectionValue_withLocalPeer} from REGISTER REQ {req.RequesterRegistrationId} to {localDrpPeer.Configuration.LocalPeerRegistrationId}", req, localDrpPeer);
                if (maxP2pConnectionValue == null || p2pConnectionValue_withLocalPeer > maxP2pConnectionValue)
                {
                    maxP2pConnectionValue = p2pConnectionValue_withLocalPeer;
                    proxyToDestinationPeer = null;
                    acceptAt = localDrpPeer;
                }
            }            
        }

        public ConnectionToNeighbor RouteInviteRequest(LocalDrpPeer localDrpPeer, InviteRequestPacket req)
        {
            ConnectionToNeighbor r = null;
            RegistrationIdDistance minDistance = null;
            foreach (var connectedPeer in localDrpPeer.ConnectedNeighbors.Where(x => x.CanBeUsedForRouting))
            {
                var distanceToConnectedPeer = req.ResponderRegistrationId.GetDistanceTo(_cryptoLibrary, connectedPeer.RemoteRegistrationId, NumberOfDimensions);
                WriteToLog_routing_detail($"distanceToConnectedPeer={distanceToConnectedPeer} from INVITE REQ {req.ResponderRegistrationId} to {connectedPeer.RemoteRegistrationId}", req, localDrpPeer);
                if (minDistance == null || minDistance.IsGreaterThan(distanceToConnectedPeer))
                {
                    minDistance = distanceToConnectedPeer;
                    r = connectedPeer;
                }
            }
            if (r == null) throw new NoNeighborsForRoutingException();
            return r;
        }
    }
}
