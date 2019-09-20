using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
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
        internal void RouteRegistrationRequest(LocalDrpPeer receivedAtLocalDrpPeerNullable, RegisterRequestPacket req, out ConnectionToNeighbor proxyToDestinationPeer, out LocalDrpPeer acceptAt)
        {
            proxyToDestinationPeer = null;
            acceptAt = null;
            RegistrationIdDistance minDistance = null;
            if (receivedAtLocalDrpPeerNullable != null)
            {
                RouteRegistrationRequest_LocalDrpPeerIteration(receivedAtLocalDrpPeerNullable, req, ref minDistance, ref proxyToDestinationPeer, ref acceptAt);
            }
            else
            {
                foreach (var localDrpPeer in LocalPeers.Values)
                    RouteRegistrationRequest_LocalDrpPeerIteration(localDrpPeer, req, ref minDistance, ref proxyToDestinationPeer, ref acceptAt);
            }

            if (proxyToDestinationPeer != null)
                WriteToLog_routing_detail($"proxying registration to {proxyToDestinationPeer}");
            else if (acceptAt != null)
                WriteToLog_routing_detail($"accepting registration at local DRP peer {acceptAt}");

            if (minDistance == null) throw new Exception();
        }
        void RouteRegistrationRequest_LocalDrpPeerIteration(LocalDrpPeer localDrpPeer, RegisterRequestPacket req,
            ref RegistrationIdDistance minDistance,
            ref ConnectionToNeighbor proxyToDestinationPeer, ref LocalDrpPeer acceptAt)
        {
            foreach (var connectedPeer in localDrpPeer.ConnectedNeighbors)
            {
                var distanceToConnectedPeer = req.RequesterRegistrationId.GetDistanceTo(_cryptoLibrary, connectedPeer.RemotePeerPublicKey);
                WriteToLog_routing_detail($"distanceToConnectedPeer={distanceToConnectedPeer} from REGISTER REQ {req.RequesterRegistrationId} to {connectedPeer.RemotePeerPublicKey}");
                if (minDistance == null || minDistance.IsGreaterThan(distanceToConnectedPeer))
                {
                    minDistance = distanceToConnectedPeer;
                    proxyToDestinationPeer = connectedPeer;
                    acceptAt = null;
                }
            }
            var distanceToLocalPeer = req.RequesterRegistrationId.GetDistanceTo(_cryptoLibrary, localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationId);
            WriteToLog_routing_detail($"distanceToLocalPeer={distanceToLocalPeer} from REGISTER REQ {req.RequesterRegistrationId} to {localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationId}");
            if (minDistance == null || minDistance.IsGreaterThan(distanceToLocalPeer))
            {
                minDistance = distanceToLocalPeer;
                proxyToDestinationPeer = null;
                acceptAt = localDrpPeer;
            }
        }

        public ConnectionToNeighbor RouteInviteRequest(LocalDrpPeer localDrpPeer, InviteRequestPacket req)
        {
            ConnectionToNeighbor r = null;
            RegistrationIdDistance minDistance = null;
            foreach (var connectedPeer in localDrpPeer.ConnectedNeighbors)
            {
                var distanceToConnectedPeer = req.ResponderRegistrationId.GetDistanceTo(_cryptoLibrary, connectedPeer.RemotePeerPublicKey);
                WriteToLog_routing_detail($"distanceToConnectedPeer={distanceToConnectedPeer} from INVITE REQ {req.ResponderRegistrationId} to {connectedPeer.RemotePeerPublicKey}");
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
