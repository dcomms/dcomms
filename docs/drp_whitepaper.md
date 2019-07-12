LICENSE: GPLv3 + custom message from developer

# Decentralized Routing Protocol

Whitepaper. Draft.

Author: Sergei Aleshin Vladimirovich. asv@startrinity.com  startrinity.asv@gmail.com https://www.linkedin.com/in/sergey-aleshin-startrinity/

## Abstract

The Decentralized Routing Protocol (DRP) is designed to build peer-to-peer (P2P) networks where peers can contact each other without servers. The DRP runs over UDP protocol and uses technique of "UDP hole punching" for NAT/firewall traversal. DRP is used to initialize direct UDP channel between two peers, it does not carry communications between peers (its purpose is similar to SIP protocol). The DRP is designed to build secure decentralized messengers, unified communication apps. In 2019 main competitors are: matrix.org, bitmessage.org, tox messenger. 















## Concepts

### User's identifiers. ID, PublicKey, PrivateKey

**user**: a person who uses the messenger. Prototype legitimate users: A=Alice, B=Bob, N=Neighbor. Unkown users: X=attacker.

**keypair** of *user* A: {PubA, PrivA} is used as a self-signed certificate of the *user*. Private key is used to sign, public key - to verify packets. We select Ed25519 ECDSA for the asymmetric crypography.

**idNonce**: random bytes (salt), is used to change user's ID without changing his public key

**ID**=SHA512(*PublicKey*|*idNonce*), 512 bits

ID is used to deliver packets across the P2P network, to verify the packets.

### ID space, distance, vectors

The ID is splitted into groups of bits, each group indicates a coordinate in 8-D **IDspace**. **Distance** between two IDs is defined as Euclidean distance. **Vector** from IDa to IDb is defined in same way as in Euclidean geometry.

### Neighborhood, routing, priorities, flood

**Connection** between **neighbor** peers A and N is a direct, bidirectional UDP channel, used to transfer DRP packets. Peer A is interested to connect with peers who are close to its own ID. 

**Routing** of **packet** at peer X to towards destination B is retransmission (proxying) of the packet towards next neighbor Y (hop) according to vector {XB}. The routing algorithm uses following concepts:

- **Rate**: number of messages going through the P2P connection in a certain period of time, speed of proxied messages. **Outgoing rate** = speed of messages **from this peer** A to neighbor N. **Incoming rate** = speed of messages **to this peer** A from neighbor N.
- **Rate limit**: maximal value of outgoing/incoming rate. Incoming rate is limited by this peer(A), outgoing rate is limited by neighbor peer (N). 
- **Rating of neighbor** N, qualified by this peer A -  personal/private opinion about the neighbor N. Components of the rating:
  -  is he legitimate user or a peer in botnet?
  - is he spammer or not? what is proportion between outgoing and incoming rates?
  - does he proxy packets correctly and deliver the packets correctly, or does he drop the packets? what is average delivery time and success rate?
  - what is RTT of pings and ping packet loss percentage?
  - how long is the connection up?
- **Rating of packet**  - is sender of the packet in contact book?  is the packet signed  by CA?
- **Flood of connection** from A to N - situation when rate of packets over the connection reaches its limit. 
- **Flood of peer** A - situation when A receives a packet to proxy, but does not have non-flooded connections to pass the packet further.

Every connection from A to neighbor N has following fields: { IDn, ratingN, recentOutRate, maxOutRate, recentInRate, maxInRate }. 

### Contact book entries

B=Bob=some other user

contact_book_entry = { IDb, PublicKeyB ??????, array of idNonceB }



## Packets, fields

Detailed explanation of the packets is below, see "Stages" section. xxxACK packet is sent in response to xxx packet, it is a part of UDP-packet loss-retransmission transport level.

**RegisterRequest** { IPa, IDa, PoW }, ?? ts,signatureA, caPubKey,caSignature

**RegisterResponse** { IPm, IDm+rnd, statusCode }

**PING** { ???IDa }

**INVITE** { IPa, IDa, PubA????, IDnonceA, IDb, PoW, ts, signA, nhops }, 

InviteACK  { IPn, IDa, PubA, IDb, PoW, ts, signA, nhops, status }, 

InviteResponse { PubB??? IDsaltB???}

InviteResponseACK











## Stages

### Generation of keypair and ID

Peer generates **keypair**. Stores the keypair in safe place. Generates one or multiple **idNonce** (salt) and **IDa**.

### Exchange of IDs

Peers A gets ID of peer B via QR code (this allows private IDb) or via public channel (another messenger, forum), adds new entry into contact book.

### Regitration/Settlement. Rendezvous Peers.

Peer A first gets connected to **Rendezvous Peer (RP)** - a peer with accessible IP address and UDP port, entry into the network. RP is connected with multiple peers; peer A asks RP to connect to neighbors near IDa within IDspace. RP connects A to intermediate peers that is closer to IDa, finally peer A gets connected to neighbors: peer A asks the network "can I settle to my IDa?", the network spreads the **registerRequest** packet to correct neighbors; some neighbors reply and get connected to peer A.  Peer A is able to increase or decrease number of connections to neighborhood. **UDP hole punching** technique is used to set up direct **UDP connections** between neighbor peers (see ref.).

PeerA sends *registerRequest* to RP. RP selects peer M who is closer to IDa+rnd. RP proxies *registerRequest* to peerM. PeerM responds with *registerResponse*  to RP, RP proxies response to A. The *registerRequest* packet is similar to REGISTER packet in SIP protocol (see ref.).

Peers A and M start to send **ping** packets to each other, keeping UDP connection open. *Ping* packets are sent on timeout, if no other packets are sent between neighbors. The *ping* packet is similar to OPTIONS packet in SIP protocol (see ref.).



### Testing performance of neighbors

Every peer is interested to measure quality of his neighbors. He sends test messages to some (random) destination IDs, receives  and measures packet loss and round-trip time (RTT)  (process similar to tracert). 

### Invite

Sent from A to B via the P2P network, from one neighbor to another, to establish *direct channel*. The lookup packet is similar to INVITE packet in SIP protocol (see ref.).

### Direct channel

When both users A and B agree to set up **direct channel**, they disclose IP addresses to each other and start end-to-end encrypted communication over some UDP-based protocol (DTLS, SRTP). 

























## Attacks and countermeasures

### DRP-level INVITE DDoS attacks on target ID

DDoS attack on target user A is possible when an attacker owns a botnet and knows ID of user A. He is able to register many fake users X talking to each other normally, then at some point send packets towards A, in this way flooding *IDspace* near location of A. **Countermeasures:** 

- **Money-based proof of work**: sell "high-priority" signatures, linked to public keys. The signatures can be used to prioritize routing of packets from user who paid for the service. Such high-priority packets will pass through flooded area in the *IDspace*.
- Frequently **change IDsalt** and synchronize it with contacts; have unique IDsalt per contact book entry; exchange IDs in safe place, so only A and B know about IDsalt's, IDa, IDb.
- Set up *direct channel* before the DoS attack. The *direct channel* could be opened before any communication between users, in case if users are afraid of possible future DRP-level DoS attack, when they will not be able to send/receive *lookup* packets.

### DRP-level DDoS on entire network

DDoS attack on entire P2P network(s) is possible if an attacker operates a botnet of peers X and at some time synchronously the peers X start sending some packets - INVITEs or REGISTERs. **Countermeasures:**

- require some work done by new neighbors: 
  - have new neighbor reply to CAPTCHA when entering into network. request CAPTCHA at RP.
  - have the new neighbor get good rating by letting it deliver some test packets
- use private P2P networks: own rendezvous servers and/or own LAN; 
- have P2P network running privately: private RPs and/or private LAN;
- have private CA who signs public keys of trusted (non-hacker) users, and packets from such signed users are routed with higher priority

### Bad neighborhood attack (Sybil attack)

before any attack: behave like legitimate peer for some time

drop 

malform 

replay (detect and drop duplicates) 

relay 

delay INVITEs or REGISTERs

### DRP-level attacks without botnet

**Countermeasures:** CPU-based proof of work

### Contact book sniffing

The attack is possible if an attacker runs a "sensor network" which records pairs of source and destination IDs, to get track source and destination users. **Countermeasures:** frequently change IDsalt and synchronize it with contacts; have unique IDsalt per contact book entry; exchange IDs in safe place, so only A and B know about IDsalt's, IDa, IDb; send many testINVITEs to some unknown IDs (not in contact book) so it will be not possible to understand is it realINVITE or testINVITE.

### Target user IP sniffing

The attacker who is interested in getting IP address of target user can generate an ID in IDspace that is close to target ID, become his neighbor (settle at target location) and get IP address of the target. **Countermeasures:** use unique temporary IDs, settle and connect to neighbors, don't connect to new neighbors after exposing the temporary ID to some potentially bad users; 

### UDP-level DDoS attacks

When an attacker knows IP address of target, he can run a regular UDP flooding attack over target IP. Cases:

- target IP address is is rendezvous peer. **Resolutions:** use DDoS-resistant hosting providers to host RPs; dynamically change IP addresses of the RPs (exchange servers, deploy RP using automation); use CAPTCHA

- target IP address is user's peer. **Resolutions:** change internet service provider; use only temporary IDsalt values and/or don't disclose IDsalt. See also: "Target user IP sniffing"

### UDP-level sniffers and blocks by ISP

An attacker or internet service provider is able to sniff IP traffic, intercept and manipulate DRP packets, block connections to peers. **Resolutions:** use VPN; encrypt DRP packets with some cipher (e.g. XOR, RC4, AES256) with secret keys; use random UDP port numbers; use another internet service provider.

### Stolen keypair

If an attacker gets access to user's device, he is able to copy his keypair. **Resolutions:** encrypt the keypair with a password; store *keypair* at hardware (cold wallet) or paper, in physically safe place(s).

### Lost keypair

An attacker can steal user's device (or user can lose/destroy the device himself) and lose access to the *keypair*. **Resolutions:** store *keypair* at hardware (cold wallet) or paper, in physically safe place(s). Multiple places could be used for redundancy.

### Stolen contact book

An attacker is able to copy user's contact book including names of contacts and their public keys, if contact book is stored at unsafe place.

### Stolen message history

**Resolutions:** do not store messages; encrypt messages with user's password.



































## References

Here is list of concepts, protocols, algorithms, used in the DRP.

- UDP protocol

- UDP hole punching
- SIP protocol
- DHT

- CPU-based Proof of Work, hashcash
- Double Ratchet Algorithm
- Bitcoin
- Elliptic Curves, Asymmetric Cryptography. Ed25519.

