using Dcomms.DSP;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dcomms.DRP
{
    public class DrpPeerEngine: IDisposable
    {
        bool _disposing;
        Thread _engineThread;
        Thread _receiverThread;
        UdpClient _socket;
        
        public DrpPeerEngine(DrpPeerEngineConfiguration configuration)
        {
            // todo  open udp socket, start receiver, engine thread

        }
        public void Dispose()
        {
        }
        void HandleException(Exception exc, string description)
        {
            //todo report to log/dev vision
        }

        Dictionary<RegistrationPublicKey, LocalDrpPeer> LocalPeers;
   

        #region registration client-side
        /// <summary>
        /// returns control only when LocalrpPeer is registered and ready for operation ("local user logged in")
        /// </summary>
        public async Task<LocalDrpPeer> RegisterAsync(DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerUser user)
        {
            // todo

            //  add new LocalDrpPeer to LocalPeers list

            var localPublicIp = await SendPublicApiRequestAsync("http://api.ipify.org/");
            if (localPublicIp == null) localPublicIp = await SendPublicApiRequestAsync("http://ip.seeip.org/");
            if (localPublicIp == null) localPublicIp = await SendPublicApiRequestAsync("http://bot.whatismyipaddress.com");
            if (localPublicIp == null) throw new Exception("Failed to resolve public IP address. Please check your internet connection");

            new RegisterPow1RequestPacket();

            // if specified rendezvous servers:
            //    send register pow request
            //    wait for response
            //    connect to neighbor
            //    on error or timeout try next rendezvous server

        }
        /// <returns>bytes of IP address</returns>
        async Task<byte[]> SendPublicApiRequestAsync(string url)
        {
            try
            {
                var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(3);
                var response = await httpClient.GetAsync(url);
                var result = await response.Content.ReadAsStringAsync();
                var ipAddress = IPAddress.Parse(result);
                return ipAddress.GetAddressBytes();
            }
            catch (Exception exc)
            {
                HandleException(exc, $"public api request to {url} failed");
                return null;
            }
        }
        #endregion

        void ProcessUdpPacket(IPEndPoint remoteEndpoint, byte[] data) // receiver thread
        {
            // parse packet
            // if packet from new peer (register syn)
            //    process register pow  like in ccp
            // if packet is from existing connected peer (ping, proxied invite/register)
            //   see which peer sends this packet by streamID, authentcate by source IP:port and  HMAC
            //   update and limit rx packet rate - blacklist regID
            //   process register requests:
            //     see if local peer is good neighbor, reply
            //     decrement nhops, check if it is not 0, proxy to some neighbor:
            //        subroutine create requestViaConnectedPeer
            //   process invite:
            //     see if local peer is destination, pass to user, reply; set up DirectChannel
            //     decrement nhops, check if it is not 0, proxy to some neighbor:
            //        subroutine create requestViaConnectedPeer

            //   process invite/register reponses: pass to original requester, verify responder signature and update rating
            //     when request is complete, clean state

            //   process ping request/response: reply; measure RTT
        }

        void OnTimer_1s() // engine thread
        {
            // for every connected peer
            //   update IIR counters for rates
            //   send ping in case of inactivity
            //   remove dead connected peers (no reply to ping)
            //   retransmit packets
            //   clean timed out requests, raise timeout events

            // expand neighborhood

            // send test packets between local registered peers
        }

        public void BeginSendInvite(RegistrationPublicKey localPeerRegistrationPublicKey, RegistrationPublicKey remotePeerRegistrationPublicKey, byte[] message, Action<DrpResponderStatusCode> callback)
        {
            // find RegisteredLocalDrpPeer

            // find closest neighbor to destination

            // send invite

            // subroutine create requestViaConnectedPeer
        }

        /// <summary>
        /// creates an instance of TxRequestState, starts retransmission timer
        /// </summary>
        bool TrySendRequestViaConnectedPeer(ConnectedDrpPeer connectedPeer, RegistrationPublicKey remotePeerRegistrationPublicKey)
        {
            // assert tx rate is not exceeded  -- return false

            // create an instance of TxRequestState, add it to list

            // send packet to peer

            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// "contact point" of local user in the regID space
    /// can be "registered" or "registering"
    /// </summary>
    public class LocalDrpPeer
    {
        LocalDrpPeerState State;
        public LocalDrpPeer(DrpPeerRegistrationConfiguration registrationConfiguration, IDrpRegisteredPeerUser user)
        {

        }
        List<ConnectedDrpPeer> ConnectedPeers; // neighbors
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
    enum LocalDrpPeerState
    {
        requestingPublicIp,
        pow,
        registerSynSent,
        pingEstablished,
        minNeighborsCountAchieved,
        achievedGoodRatingForNeighbors // ready to send requests
    }


    public class DrpPeerEngineConfiguration
    {
        ushort? LocalPort;
    }
    public class DrpPeerRegistrationConfiguration
    {
        IPEndPoint[] RendezvousPeers; // in case when local peer IP = rendezvous peer IP, it is skipped
        RegistrationPublicKey LocalPeerRegistrationPublicKey;
        RegistrationPrivateKey LocalPeerRegistrationPrivateKey;
        int NumberOfNeighborsToKeep;
    }

    public interface IDrpRegisteredPeerUser
    {
        void OnReceivedMessage(byte[] message);
    }

    enum DrpPeerConnectionInitiatedBy
    {
        localPeer, // local peer connected to remote peer via REGISTER procedure
        remotePeer // re mote peer connected to local peer via REGISTER procedure
    }
    class ConnectedDrpPeer
    {
        DrpPeerConnectionInitiatedBy InitiatedBy;
        RegistrationPublicKey RemotePeerPublicKey;
        SecretHeyForHmac SecretKeyForHmac;

        ConnectedDrpPeerRating Rating;

        IirFilterCounter RxInviteRateRps;
        IirFilterCounter RxRegisterRateRps;

        float MaxTxInviteRateRps, MaxTxRegiserRaateRps; // sent by remote peer via ping
        IirFilterCounter TxInviteRateRps, TxRegisterRateRps;
        List<TxRegisterRequestState> PendingTxRegisterRequests;
        List<TxInviteRequestState> PendingTxInviteRequests;
    }
    class ConnectedDrpPeerRating
    {
        IirFilterAverage PingRttMs;
        TimeSpan Age => throw new NotImplementedException();
        float RecentRegisterRequestsSuccessRate => throw new NotImplementedException(); // target of sybil-looped-traffic attack
        float RecentInviteRequestsSuccessRate => throw new NotImplementedException(); // target of sybil-looped-traffic attack
    }
    /// <summary>
    /// contains recent RDRs;  RAM-based database
    /// is used to prioritize requests in case of DoS
    /// 50 neighbors, 1 req per second, 1 hour: 180K records: 2.6MB
    /// </summary>
    class RequestDetailsRecordsHistory
    {
        LinkedList<RequestDetailsRecord> Records; // newest first
    }
    class RequestDetailsRecord
    {
        RegistrationPublicKey Sender;
        RegistrationPublicKey Receiver;
        RegistrationPublicKey Requester;
        RegistrationPublicKey Responder;
        DateTime RequestCreatedTimeUTC;
        DateTime RequestFinishedTimeUTC;
        DrpResponderStatusCode Status;
    }
    enum RequestType
    {
        invite,
        register
    }

    class TxRequestState
    {
        DateTime WhenCreatedUTC; // is taken from local clock
        DateTime LatestTxUTC; // is taken from local clock
        int TxPacketsCount; // is incremented when UDP packet is retransmitted

        ConnectedDrpPeer ReceivedFrom;
        ConnectedDrpPeer ProxiedTo;
    }

    class TxRegisterRequestState: TxRequestState
    {
        RegistrationPublicKey RequesterPublicKey_RequestID;
    }

    class TxInviteRequestState: TxRequestState
    {
        // requestID={RequesterPublicKey|DestinationResponderPublicKey}
        RegistrationPublicKey RequesterPublicKey; // A public key 
        RegistrationPublicKey DestinationResponderPublicKey; // B public key
    }
}
