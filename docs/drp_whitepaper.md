LICENSE: GPLv3

# Decentralized Routing Protocol

Whitepaper. Draft.

Author: Sergei Aleshin Vladimirovich. asv@startrinity.com  startrinity.asv@gmail.com https://www.linkedin.com/in/sergey-aleshin-startrinity/

## Abstract

The Decentralized Routing Protocol (DRP) is designed to build peer-to-peer (P2P) networks where peers can contact each other without servers. The DRP runs over UDP protocol and uses technique of "UDP hole punching" for NAT/firewall traversal. DRP is used to initialize direct UDP channel between two peers, it does not carry communications between peers. The DRP is designed to build secure decentralized messengers,  UC apps. In 2019 main competitors are: matrix.org, bitmessage.org, tox messenger. 







## Concepts

### User's identifiers. ID, PublicKey, PrivateKey

**user**: a person who uses the messenger.

**keypair**: {PublicKey, PrivateKey} is used as a self-signed certificate of a *user*. Private key is used to sign, public key - to verify packets. We select Ed25519 ECDSA for the asymmetric crypography.

**idNonce**: random bytes (salt), is used to change user's ID without changing his public key

**ID**=SHA512(*PublicKey*|*idNonce*), 512 bits

ID is used to deliver packets across the P2P network, to sign transmitted packets, verify the packets.

### ID space, distance, vectors

The ID is splitted into groups of bits, each group indicates a coordinate in 8-D **IDspace**. **Distance** between two IDs is defined as Euclidean distance. **Vector** from IDa to IDb is defined in same way as in Euclidean geometry.

### Neighborhood, routing, priorities

**Connection** between **neighbor** peers A and N is a direct, bidirectional UDP channel, used to transfer DRP packets. Peer A is interested to connect with peers who are close to its own ID. 

**Routing** of **packet** at peer X to towards destination B is retransmission (proxying) of the packet towards next neighbor Y (hop) according to vector {XB}. The routing algorithm uses following concepts:

- **Rate**: number of messages going through the P2P connection in a certain period of time, speed of proxied messages. **Outgoing rate** = speed of messages **from this peer** A to neighbor N. **Incoming rate** = speed of messages **to this peer** A from neighbor N.
- **Rate limit**: maximal value of outgoing/incoming rate. Incoming rate is limited by this peer(A), outgoing rate is limited by neighbor peer (N). 
- **Rating of neighbor** N, qualified by this peer A -  personal/private opinion about the neighbor N - is he legitimate user or a botnet used for DoS, is he spammer or not, does he proxy packets correctly or does he drop the packets.
- **Rating of packet**  - is sender of the packet in contact book?  is the packet signed  by CA?
- **Flood of connection** from A to N - situation when rate of packets over the connection reaches its limit. 
- **Flood of peer** A - situation when A receives a packet to proxy, but does not have non-flooded connections to pass the packet further.

Every connection from A to neighbor N has following fields: { IDn, ratingN, recentOutRate, maxOutRate, recentInRate, maxInRate }. 

### Contact book entries

B=Bob=some other user

contact_book_entry = { IDb, PublicKeyB, idNonceB }



## Packets, fields

**SettlementRequest** { IPa, IDa+rnd, timestamp, PoW }, ?? ts,signatureA, caPubKey,caSignature

rnd is a random vector, used to hide IDa.

SettlementResponse { IPm, IDm+rnd, }

InitialLookup, InitialLookupACK, Response





## Stages

### Generation of keypair and ID

Peer generates keypair, idNonce, IDa. Stores the keypair in safe place.

### Exchange of IDs

Peers A gets ID of peer B via QR code (this allows private IDb) or via public channel (another messenger, forum), adds new entry into contact book.

### Settlement. Rendezvous Nodes.

Peer A first gets connected to **Rendezvous Node (RN)** - a peer with accessible IP address and UDP port, entry into the network. RN is connected with multiple peers; peer A asks RN to connect to neighbors near IDa within IDspace. RN connects A to intermediate peers that is closer to IDa, finally peer A gets connected to neighbors. Peer A is able to increase or decrease number of connections to neighborhood. UDP hole punching technique is used to create new connections between peers.

PeerA sends *settlementRequest* to RN. RN selects peer M who is closer to IDa+rnd. RN proxies settlementRequest to peerM. PeerM respond with settlementResponse:OK/reject

RN responds   to peerA: settlementResponse {IPm,IPx}

peerA asks "can I settle to my IDa?";
the network spreads the "settlementRequest" signal to corect neighbours; 
some neighbours reply and get connected to peerA

### Testing neighbors

Every peer is interested to measure quality of his neighbors. He sends test messages to some (random) destinations and measures packet loss and round-trip time (RTT).

### Initial Lookup

from A to B, to add to contact book.

### Subsequent Lookup

lookup with preshared key in the contact book

### Direct UDP channel

When both users A and B agree to set up **direct channel**, they disclose IP addresses to each other and start end-to-end encrypted communication over some other protocol (DTLS, SIP, RTP).

The direct channel could be opened before any communication between users, in case if users are afraid of possible future DRP-level DoS attack, when they will not be able to set up the direct channel.





## Usage modes

- Public P2P network
- Private P2P network, with own rendezvous node(s). Rendezvous nodes accessible from public internet, or accessible from LAN only



## Attacks and countermeasures

### DRP-level DDoS attacks

DDoS attack on target user A is possible when an attacker owns a botnet and knows ID of user A. He is able to register many fake users X talking to each other normally, then at some point send oackets towards A, in this way flooding *IDspace* near location of A.

### Contact book sniffing

The attack is possible if an attacker runs a "sensor network" which records pairs of source and destination IDs.

### Target IP sniffing

The attacker who is interested in getting IP address of target user can generate an ID in IDspace that is close to target ID, become his neighbor (settle at target location) and get IP address of the target.

### UDP-level DDoS attacks

When an attacker knows IP address of target, he can run a regular UDP flooding attack over target IP.







