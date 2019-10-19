﻿using Dcomms.DRP.Packets;
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

            if (proxyToDestinationPeer != null)
            {
                WriteToLog_routing_higherLevelDetail($"proxying registration to {proxyToDestinationPeer}");
                return true;
            }
            else if (acceptAt != null)
            {
                WriteToLog_routing_higherLevelDetail($"accepting registration at local DRP peer {acceptAt}");
                return true;
            }
            else
            {
                WriteToLog_routing_needsAttention($"no route found for REGISTER request to {req.RequesterRegistrationId}");
                return false;
            }
        }




        void RouteRegistrationRequest_LocalDrpPeerIteration(LocalDrpPeer localDrpPeer, ConnectionToNeighbor sourceNeighborNullable,
            HashSet<ConnectionToNeighbor> alreadyTriedProxyingToDestinationPeersNullable,
            RegisterRequestPacket req,
            ref double? maxP2pConnectionValue,
            ref ConnectionToNeighbor proxyToDestinationPeer, ref LocalDrpPeer acceptAt)
        {
            var connectedNeighborsForRouting = new List<ConnectionToNeighbor>();
            foreach (var connectedPeer in localDrpPeer.ConnectedNeighbors)
            {
                if (sourceNeighborNullable != null && connectedPeer == sourceNeighborNullable)
                {
                    WriteToLog_routing_detail($"skipping routing back to source peer {connectedPeer.RemoteRegistrationId}");
                    continue;
                }
                if (alreadyTriedProxyingToDestinationPeersNullable != null && alreadyTriedProxyingToDestinationPeersNullable.Contains(connectedPeer))
                {
                    WriteToLog_routing_detail($"skipping routing to previously tried peer {connectedPeer.RemoteRegistrationId}");
                    continue;
                }

                if (req.RequesterRegistrationId.Ed25519publicKey.Equals(connectedPeer.RemoteRegistrationId))
                {
                    WriteToLog_routing_detail($"skipping routing to peer with same regID {connectedPeer.RemoteRegistrationId}");
                    continue;
                }

                connectedNeighborsForRouting.Add(connectedPeer);
            }


            int connectedNeighborsCountThatMatchMinDistance = 0;
            foreach (var neighbor in connectedNeighborsForRouting)
            {
                var distanceToNeighbor = req.RequesterRegistrationId.GetDistanceTo(_cryptoLibrary, neighbor.RemoteRegistrationId);
                WriteToLog_routing_detail($"distanceToConnectedPeer={distanceToNeighbor} from REGISTER REQ {req.RequesterRegistrationId} to {neighbor.RemoteRegistrationId} (req.min={req.MinimalDistanceToNeighbor})");
                if (req.MinimalDistanceToNeighbor != 0)
                {
                    if (distanceToNeighbor.IsLessThan(req.MinimalDistanceToNeighbor))
                    {
                        // skip: this is too close than requested
                        WriteToLog_routing_detail($"skipping connection to {neighbor.RemoteRegistrationId}: distance={distanceToNeighbor} is less than requested={req.MinimalDistanceToNeighbor}");
                        continue;
                    }
                }
                var p2pConnectionValue_withNeighbor = P2pConnectionValueCalculator.GetMutualP2pConnectionValue(CryptoLibrary, req.RequesterRegistrationId, req.RequesterNeighborsBusySectorIds, neighbor.RemoteRegistrationId, neighbor.RemoteNeighborsBusySectorIds);
                               
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
                RegistrationIdDistance maxDistance = null;
                foreach (var connectedPeer in connectedNeighborsForRouting)
                {         
                    var distanceToConnectedPeer = req.RequesterRegistrationId.GetDistanceTo(_cryptoLibrary, connectedPeer.RemoteRegistrationId);
                    WriteToLog_routing_detail($"distanceToConnectedPeer={distanceToConnectedPeer} from REGISTER REQ {req.RequesterRegistrationId} to {connectedPeer.RemoteRegistrationId} (req.min={req.MinimalDistanceToNeighbor})");
                   
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
                WriteToLog_routing_detail($"not accepting request at local peer: it has same regID {req.RequesterRegistrationId}");
            }
            else if (localDrpPeer.ConnectedNeighbors.Any(x => x.RemoteRegistrationId.Equals(req.RequesterRegistrationId)) == true)
            {
                WriteToLog_routing_detail($"not accepting request at local peer: already connected to {req.RequesterRegistrationId}");
            }
            else
            {
                var p2pConnectionValue_withLocalPeer = P2pConnectionValueCalculator.GetMutualP2pConnectionValue(CryptoLibrary, req.RequesterRegistrationId, req.RequesterNeighborsBusySectorIds, localDrpPeer.Configuration.LocalPeerRegistrationId, localDrpPeer.ConnectedNeighborsBusySectorIds);
                WriteToLog_routing_detail($"valueOfLocalPeer={p2pConnectionValue_withLocalPeer} from REGISTER REQ {req.RequesterRegistrationId} to {localDrpPeer.Configuration.LocalPeerRegistrationId}");
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
            foreach (var connectedPeer in localDrpPeer.ConnectedNeighbors)
            {
                var distanceToConnectedPeer = req.ResponderRegistrationId.GetDistanceTo(_cryptoLibrary, connectedPeer.RemoteRegistrationId);
                WriteToLog_routing_detail($"distanceToConnectedPeer={distanceToConnectedPeer} from INVITE REQ {req.ResponderRegistrationId} to {connectedPeer.RemoteRegistrationId}");
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
