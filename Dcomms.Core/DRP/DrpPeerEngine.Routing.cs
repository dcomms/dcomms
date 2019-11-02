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
        /// is null in A-EP mode
        /// </param>
        internal bool RouteRegistrationRequest(LocalDrpPeer receivedAtLocalDrpPeerNullable, RoutedRequest routedRequest, out ConnectionToNeighbor proxyToDestinationPeer, out LocalDrpPeer acceptAt)
        {
            proxyToDestinationPeer = null;
            acceptAt = null;

            var req = routedRequest.RegisterReq;
            var logger = routedRequest.Logger;

            if (req.NumberOfHopsRemaining <= 1)
            {
                var possibleAcceptAt = receivedAtLocalDrpPeerNullable ?? routedRequest.ReceivedFromNeighborNullable?.LocalDrpPeer;
                if (possibleAcceptAt == null)
                    possibleAcceptAt = LocalPeers.Values.Where(x => x.Configuration.LocalPeerRegistrationId.Equals(req.RequesterRegistrationId) == false).FirstOrDefault();

                if (possibleAcceptAt != null)
                {
                    if (!possibleAcceptAt.ConnectedNeighbors.Any(x => x.RemoteRegistrationId.Equals(req.RequesterRegistrationId)))
                    {
                        logger.WriteToLog_routing_higherLevelDetail($"accepting registration request {req.RequesterRegistrationId} at local DRP peer {acceptAt}: low number of hops remaining");
                        acceptAt = possibleAcceptAt;
                        return true;
                    }
                    else
                        logger.WriteToLog_routing_higherLevelDetail($"not accepting registration at local DRP peer" +
                            $" {acceptAt} when low number of hops remaining: already connected");

                }
            }

            if (req.RandomModeAtThisHop)
            { // random mode
                var itemsForRouting = new List<object>();
                if (routedRequest.ReceivedFromNeighborNullable == null)
                {
                    foreach (var localDrpPeer in LocalPeers.Values)
                    {
                        itemsForRouting.AddRange(localDrpPeer.GetConnectedNeighborsForRouting(routedRequest));
                        if (!localDrpPeer.Configuration.LocalPeerRegistrationId.Equals(req.RequesterRegistrationId))
                            itemsForRouting.Add(localDrpPeer);
                    }
                }
                else
                {
                    itemsForRouting.AddRange(routedRequest.ReceivedFromNeighborNullable.LocalDrpPeer.GetConnectedNeighborsForRouting(routedRequest));
                    if (!routedRequest.ReceivedFromNeighborNullable.LocalDrpPeer.Configuration.LocalPeerRegistrationId.Equals(req.RequesterRegistrationId))
                        itemsForRouting.Add(routedRequest.ReceivedFromNeighborNullable.LocalDrpPeer);
                }

                if (itemsForRouting.Count != 0)
                {
                    var itemForRouting = itemsForRouting[_insecureRandom.Next(itemsForRouting.Count - 1)];
                    logger.WriteToLog_routing_higherLevelDetail($"routing registration in random mode to one of {itemsForRouting.Count} destinations: {proxyToDestinationPeer}");
                    if (itemForRouting is ConnectionToNeighbor) proxyToDestinationPeer = (ConnectionToNeighbor)itemForRouting;
                    else acceptAt = (LocalDrpPeer)itemForRouting;
                }
                else
                    logger.WriteToLog_routing_needsAttention($"can not route registration in random mode: no destinations available including local peers");
            }
            else
            {
                double? maxP2pConnectionValue = null;
                if (receivedAtLocalDrpPeerNullable != null)
                {
                    RouteRegistrationRequest_LocalDrpPeerIteration(receivedAtLocalDrpPeerNullable, routedRequest, ref maxP2pConnectionValue, ref proxyToDestinationPeer, ref acceptAt);
                }
                else
                {
                    foreach (var localDrpPeer in LocalPeers.Values)
                        RouteRegistrationRequest_LocalDrpPeerIteration(localDrpPeer, routedRequest, ref maxP2pConnectionValue, ref proxyToDestinationPeer, ref acceptAt);
                }
            }

            if (proxyToDestinationPeer != null)
            {
                logger.WriteToLog_routing_higherLevelDetail($"proxying registration to {proxyToDestinationPeer}");
                return true;
            }
            else if (acceptAt != null)
            {
                logger.WriteToLog_routing_higherLevelDetail($"accepting registration at local DRP peer {acceptAt}");
                return true;
            }
            else
            {
                logger.WriteToLog_routing_higherLevelDetail($"no route found for REGISTER request to {req.RequesterRegistrationId}");
                return false;
            }
        }

        void RouteRegistrationRequest_LocalDrpPeerIteration(LocalDrpPeer localDrpPeer, RoutedRequest routedRequest,
            ref double? maxP2pConnectionValue,
            ref ConnectionToNeighbor proxyToDestinationPeer, ref LocalDrpPeer acceptAt)
        {
            var req = routedRequest.RegisterReq;
            var connectedNeighborsForRouting = localDrpPeer.GetConnectedNeighborsForRouting(routedRequest).ToList();
            var logger = routedRequest.Logger;

            int connectedNeighborsCountThatMatchMinDistance = 0;
            foreach (var neighbor in connectedNeighborsForRouting)
            {
                var distanceToNeighbor = req.RequesterRegistrationId.GetDistanceTo(_cryptoLibrary, neighbor.RemoteRegistrationId, NumberOfDimensions);
                logger.WriteToLog_detail($"distanceToNeighbor={distanceToNeighbor} from REGISTER REQ {req.RequesterRegistrationId} to {neighbor} (req.min={req.MinimalDistanceToNeighbor})");
                if (req.MinimalDistanceToNeighbor != 0)
                {
                    if (distanceToNeighbor.IsLessThan(req.MinimalDistanceToNeighbor))
                    {
                        // skip: this is too close than requested
                        logger.WriteToLog_detail($"skipping connection to {neighbor}: distance={distanceToNeighbor} is less than requested={req.MinimalDistanceToNeighbor}");
                        continue;
                    }
                }
                var p2pConnectionValue_withNeighbor = P2pConnectionValueCalculator.GetMutualP2pConnectionValue(CryptoLibrary, req.RequesterRegistrationId, req.RequesterNeighborsBusySectorIds,
                    neighbor.RemoteRegistrationId, neighbor.RemoteNeighborsBusySectorIds ?? 0, NumberOfDimensions);

                logger.WriteToLog_detail($"p2pConnectionValue_withNeighbor={p2pConnectionValue_withNeighbor} from REGISTER REQ {req.RequesterRegistrationId} to {neighbor}");

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
                logger.WriteToLog_detail($"special case: move away from the requester, proxy to most distant neighbor");

                RegistrationIdDistance maxDistance = null;
                foreach (var connectedPeer in connectedNeighborsForRouting)
                {         
                    var distanceToConnectedPeer = req.RequesterRegistrationId.GetDistanceTo(_cryptoLibrary, connectedPeer.RemoteRegistrationId, NumberOfDimensions);
                    logger.WriteToLog_detail($"distanceToConnectedPeer={distanceToConnectedPeer} from REGISTER REQ {req.RequesterRegistrationId} to {connectedPeer.RemoteRegistrationId} (req.min={req.MinimalDistanceToNeighbor})");
                   
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
                logger.WriteToLog_detail($"not accepting request at local peer: it has same regID {req.RequesterRegistrationId}");
            }
            else if (localDrpPeer.ConnectedNeighbors.Any(x => x.RemoteRegistrationId.Equals(req.RequesterRegistrationId)) == true)
            {
                logger.WriteToLog_detail($"not accepting request at local peer: already connected to {req.RequesterRegistrationId}");
            }
            else if (localDrpPeer.ConnectedNeighbors.Count > localDrpPeer.Configuration.AbsoluteMaxNumberOfNeighbors)
            {
                logger.WriteToLog_detail($"not accepting request at local peer: already too many neighbors ({localDrpPeer.ConnectedNeighbors.Count})");
            }
            else
            {
                var p2pConnectionValue_withLocalPeer = P2pConnectionValueCalculator.GetMutualP2pConnectionValue(CryptoLibrary, req.RequesterRegistrationId, req.RequesterNeighborsBusySectorIds, localDrpPeer.Configuration.LocalPeerRegistrationId, localDrpPeer.ConnectedNeighborsBusySectorIds, NumberOfDimensions);
                logger.WriteToLog_detail($"p2pConnectionValue_withLocalPeer={p2pConnectionValue_withLocalPeer} from REGISTER REQ {req.RequesterRegistrationId} to {localDrpPeer.Configuration.LocalPeerRegistrationId}");
                if (maxP2pConnectionValue == null || p2pConnectionValue_withLocalPeer > maxP2pConnectionValue)
                {
                    maxP2pConnectionValue = p2pConnectionValue_withLocalPeer;
                    proxyToDestinationPeer = null;
                    acceptAt = localDrpPeer;
                }
            }            
        }

        /// <returns>null if no neighbors found for routing</returns>
        public ConnectionToNeighbor RouteInviteRequest(LocalDrpPeer localDrpPeer, RoutedRequest routedRequest)
        {
            var req = routedRequest.InviteReq;
            var logger = routedRequest.Logger;
            ConnectionToNeighbor r = null;
            RegistrationIdDistance minDistance = null;
            var connectedNeighborsForRouting = localDrpPeer.GetConnectedNeighborsForRouting(routedRequest).ToList();

            foreach (var connectedPeer in connectedNeighborsForRouting)
            {
                var distanceToConnectedPeer = req.ResponderRegistrationId.GetDistanceTo(_cryptoLibrary, connectedPeer.RemoteRegistrationId, NumberOfDimensions);
                logger.WriteToLog_detail($"distanceToConnectedPeer={distanceToConnectedPeer} from {req} to {connectedPeer.RemoteRegistrationId}");
                if (minDistance == null || minDistance.IsGreaterThan(distanceToConnectedPeer))
                {
                    minDistance = distanceToConnectedPeer;
                    r = connectedPeer;
                }
            }
            if (r == null)
            {
                logger.WriteToLog_detail($"no neighbors found for routing");
            }
         
            return r;
        }
    }
}
