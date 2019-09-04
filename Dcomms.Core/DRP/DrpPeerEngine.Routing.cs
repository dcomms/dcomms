using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP
{
    partial class DrpPeerEngine
    {

        /// <summary>
        /// main routing procedure for register SYN requests
        /// </summary>
        /// <param name="receivedAtLocalDrpPeerNullable">
        /// is set when routing SYN packets that are received via P2P connection from neighbor to the LocalDrpPeer
        /// </param>
        internal void RouteRegistrationRequest(LocalDrpPeer receivedAtLocalDrpPeerNullable, RegisterSynPacket syn, out ConnectionToNeighbor proxyToDestinationPeer, out LocalDrpPeer acceptAt)
        {
            proxyToDestinationPeer = null;
            acceptAt = null;
            RegistrationPublicKeyDistance minDistance = null;
            if (receivedAtLocalDrpPeerNullable != null)
            {
                RouteRegistrationRequest_LocalDrpPeerIteration(receivedAtLocalDrpPeerNullable, syn, ref minDistance, ref proxyToDestinationPeer, ref acceptAt);
            }
            else
            {
                foreach (var localDrpPeer in LocalPeers.Values)
                    RouteRegistrationRequest_LocalDrpPeerIteration(localDrpPeer, syn, ref minDistance, ref proxyToDestinationPeer, ref acceptAt);
            }

            if (proxyToDestinationPeer != null)
                WriteToLog_routing_detail($"proxying registration to {proxyToDestinationPeer}");
            else if (acceptAt != null)
                WriteToLog_routing_detail($"accepting registration at local DRP peer {acceptAt}");

            if (minDistance == null) throw new Exception();
        }
        void RouteRegistrationRequest_LocalDrpPeerIteration(LocalDrpPeer localDrpPeer, RegisterSynPacket syn,
            ref RegistrationPublicKeyDistance minDistance,
            ref ConnectionToNeighbor proxyToDestinationPeer, ref LocalDrpPeer acceptAt)
        {
            foreach (var connectedPeer in localDrpPeer.ConnectedPeers)
            {
                var distanceToConnectedPeer = syn.RequesterPublicKey_RequestID.GetDistanceTo(_cryptoLibrary, connectedPeer.RemotePeerPublicKey);
                WriteToLog_routing_detail($"distanceToConnectedPeer={distanceToConnectedPeer} from SYN {syn.RequesterPublicKey_RequestID} to {connectedPeer.RemotePeerPublicKey}");
                if (minDistance == null || minDistance.IsGreaterThan(distanceToConnectedPeer))
                {
                    minDistance = distanceToConnectedPeer;
                    proxyToDestinationPeer = connectedPeer;
                    acceptAt = null;
                }
            }
            var distanceToLocalPeer = syn.RequesterPublicKey_RequestID.GetDistanceTo(_cryptoLibrary, localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPublicKey);
            WriteToLog_routing_detail($"distanceToLocalPeer={distanceToLocalPeer} from SYN {syn.RequesterPublicKey_RequestID} to {localDrpPeer.RegistrationConfiguration.LocalPeerRegistrationPublicKey}");
            if (minDistance == null || minDistance.IsGreaterThan(distanceToLocalPeer))
            {
                minDistance = distanceToLocalPeer;
                proxyToDestinationPeer = null;
                acceptAt = localDrpPeer;
            }
        }
    }
}
