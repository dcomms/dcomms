License is MIT: you can use this protocol and C# (.NET Standard) implementation to develop your own messenger and UC app, under your brand name.

# Decentralized Routing Protocol

Whitepaper. Draft.

Author: Sergei Aleshin Vladimirovich. asv@startrinity.com  startrinity.asv@gmail.com https://www.linkedin.com/in/sergey-aleshin-startrinity/  http://dcomms.org

## Abstract

The Decentralized Routing Protocol (DRP) is designed to build peer-to-peer (P2P) networks where peers can contact each other without servers. The DRP runs over UDP protocol and uses technique of "UDP hole punching" for NAT/firewall traversal. DRP is used to initialize direct UDP channel between two peers, it does not carry communications between peers (its purpose is similar to SIP protocol). The DRP is designed to build secure decentralized messengers, unified communication apps. 

## State of Art

SIP protocol is designed for centralized architecture.....  servers are bottlenecks ... DoS vulnerabilities ... NAT/firewall issues ... high cost of environment setup and testing

P2P networks like Tor and I2P are under DoS attacks.... frequent downtimes.... high RTT   .... success of bittorrent protocol....

In 2019 main competitors are: matrix.org, bitmessage.org, tox messenger.....

French government ... HTTPS and PKI vulnerabilities .. case of gmail in Iran ...

Quality of source code is not good ....















## Concepts

### User's identifiers, public and private keys

**user**: a person who uses the messenger. Prototype legitimate users: A=Alice, B=Bob, N=Neighbor. Unkown users: X=attacker.

**userKeypair** of *user* A: {userPubA, userPrivA} is used as a self-signed certificate of the *user*. Private key is used to sign, public key - to verify packets in direct channel (not INVITE and REGISTER). We select Ed25519 ECDSA for the asymmetric cryptography.

**regKeypair**: {regPubA, regPrivA} is used to register a user within *regIDspace*. Private key is used to sign, public key - to verify REGISTER and INVITE packets. User can replace regKeypairs without changing his *userKeypair*

**regID**=SHA512(*regPubA*), 512 bits

regID is used to route/deliver REGISTER and INVITE packets across the P2P network between registered users, to verify the packets.







### ID space, distance, vectors

The regID is splitted into groups of bits, each group indicates a coordinate in 8-D **regIDspace**. **Distance** between two regIDs is defined as Euclidean distance. **Vector** from regIDa to regIDb is defined in same way as in Euclidean geometry.

### Neighborhood, routing, priorities, flood

**Connection** between **neighbor** peers A and N is a direct, bidirectional UDP channel, used to transfer DRP packets. Peer A is interested to register and connect with peers who are close to its own regID. 

**Routing** of **packet** at peer X to towards destination B is retransmission (proxying) of the packet towards next neighbor Y (hop) according to vector {regIDb->regIDx}. The routing algorithm uses following concepts:

- **Rate**: number of messages going through the P2P connection in a certain period of time, speed of proxied REGISTER/INVITE packets. **Outgoing rate** = speed of packets **from this peer** A to neighbor N. **Incoming rate** = speed of packets **to this peer** A from neighbor N.
- **Rate limit**: maximal value of outgoing/incoming rate. Incoming rate is limited by this peer(A), outgoing rate is limited by neighbor peer (N). 
- **Rating of neighbor** N, qualified by this peer A -  personal/private opinion about the neighbor N. Components of the rating:
  -  is he legitimate user or a peer in botnet?
  - is he spammer or not? what is proportion between outgoing and incoming rates?
  - does he proxy packets correctly and deliver the packets correctly, or does he drop the packets? what is average delivery time and success rate?
  - what is RTT of PINGs and PING packet loss percentage?
  - how long is the connection up?
- **Rating of packet**  - is sender of the packet in contact book?  is the packet signed  by CA?
- **Flood of connection** from A to N - situation when rate of packets over the connection reaches its limit. 
- **Flood of peer** A - situation when A receives a packet to proxy, but does not have non-flooded connections to pass the packet further.

Every connection from A to neighbor N has following fields: { regIDn, ratingN, recentOutRate, maxOutRate, recentInRate, maxInRate }. 

### Contact book entries

B=Bob=some other user

contact_book_entry = { userPubB, array of regPubB (location of redundant registrations) }









## Packets, fields

**REGISTER** requests connection with a neighbor who is close to requester regID. **INVITE** requests direct communication with peer B, is proxied via P2P network. **PING** packets are sent between peers to keep connection alive. Elements:

- IPx = IP address and UDP port number of a peer X.
- regSignX = signature of the entire packet (except maxhops field) by peer X (private key regPrivX)

**REGISTER** { IPa, regPubA, ts, regSignA, cpuPoWa=nonceA=messageID, caNameID, caNonce, caSign, maxhops }

path of *REGISTER* request: (A->RP->M->N). The *REGISTER* packet is similar to REGISTER in SIP protocol (see ref). In order to minimize DoS attacks and to avoid IPa spoofing, *REGISTER* request from a new peer is rejected by neighbors until they measure rating of the new peer by test *REGISTER's*.

**REGISTERresponse** { IPx, statusCode={received(at neighbor),connected,rejected,maxhops}, cpuPoWa,  regPubX, nonceX, regSignX=cpuPoWx }  paths: (RP->A); (M->RP);  (N->M->RP->A )

**PING** { IPa, nonceA, signA=cpuPoWa } The *PING* packet is similar to OPTIONS packet in SIP protocol (see ref).

**INVITE** { same as for REGISTER }, The *INVITE* packet is similar to INVITE packet in SIP protocol (see ref).

**INVITEresponse**  { same as for REGISTER }

### Packets over direct channel

**AUTHandSETUPKEY** { ts, encUserA_SessionKeyAtoB, signUserA  } 

**AUTHandSETUPKEYresponse** { ts, encUserB_SessionKeyBtoA, signUserB }

Following direct-channel packets are encrypted by sessionKeyBtoA/sessionKeyAtoB, AES256 in CBC mode:

- **DC_PING** { sequenceNumber, ts, nonceA, signUserA }
- **DC_TEXTMESSAGE** { sequenceNumber, ts, plainText, nonceA, signUserA }
- **DC_AUDIO** { sequenceNumber, ts, codec, audioData } - similar to RTP (see ref)
- **DC_UPDATEREG**  { ts, array of regPubKeys, signUserA } updates entry in remote contact book with new regIDs















## Stages

### Generation of userKeypair and regIDs

Peer generates **userKeypair**. Stores the *userKeypair* in safe place. Generates one or multiple **regKeypairs** - locations in *regIDspace* for registration.

### Adding into contact book via secure channel

User A gets public key of user B *userPubB* and multiple registration locations *regPubB*  over some secure channel, adds new entry into contact book under name of user B. Same for user B. The secure channels are: 1) QR code - camera 2) trusted email servers

### Adding into contact book via insecure channel

- user B wants to add user A into his contacts
- user A gets a temporary **tempRegPubB** from user B over some insecure channel. The tempRegPubB is created specially for this single operation, it is valid for a short time, it is linked by user B to "new user A" 

- users A and B register in the network with temporary regIDs
- user A sends INVITE to *tempRegPubB*, B responds and signs the response with *tempRegPrivB*

- A and B set up *direct channel*, perform Diffie-Hellman key exchange, derive a temporary shared **session key**. Further communication is encrypted with this session key.
- users exchange some text messages to authenticate each other
- users exchange their userPubKeys and regPubKeys and store the keys in contact book

### Registration. Rendezvous Peers. Connecting to neighbors

Peer A first gets connected to **Rendezvous Peer (RP)** - a peer with accessible IP address and UDP port, entry into the network. RP is connected with multiple peers. Peer A sends REGISTER, asking RP to connect to neighbors near *regIDa* within *regIDspace*. RP spreads the REGISTER to intermediate peers that are closer to IDa, finally a neighbor N replies to REGISTER and A gets connected to N. Peer A is able to increase or decrease number of connections to neighborhood by sending more REGISTER requests to existing neighbors. **UDP hole punching** technique is used to set up **direct UDP connections** between neighbors (see ref).

Peers A and M start to send **PING** packets to each other, keeping UDP connection alive. *PING* packets are sent only if no other packets are sent between neighbors. 



### Testing performance of neighbors

Every peer is interested to measure quality of his neighbors. He sends test messages to some (random) destination regIDs, receives  and measures packet loss and round-trip time (RTT)  (this is similar to tracert, see ref.). 

### Invite

INVITE is sent from A to B via the P2P network, from one neighbor to another, to establish *direct channel*. If user B agrees to share his IP address to user A, peer B responds to INVITE.

### Direct channel

When both users A and B agree to set up **direct channel**, they share IP addresses between each other using INVITE and start end-to-end encrypted (E2EE) communication. Users authenticate each other using public keys in contact books, generate temporary session keys: 

- B generates random **sessionKeyBtoA** (unidirectional), encrypts it with userPubA, A decrypts it with userPrivA
- A generates random **sessionKeyAtoB** (unidirectional), encrypts it with userPubB, B decrypts it with userPrivB

Temporary keys are destroyed and never used again; it avoids decryption of independent sessions with same key, similarly to double ratchet algorithm, perfect forward secrecy (see ref).

















## Attacks and countermeasures

### DRP-level INVITE DDoS attack on target regID

DDoS attack on target user A is possible when an attacker owns a botnet and knows *regID* of user A. He is able to register many fake users X talking to each other normally, then at some point send packets towards A, in this way flooding *regIDspace* near location of A. **Countermeasures:** 

- Use rating of packet and sender in case of flood
- **Money-based proof of work**: sell "high-priority" signatures, linked to public keys. The signatures can be used to prioritize routing of packets from user who paid for the service. Such high-priority packets will pass through flooded area in the *regIDspace*.
- Frequently **change regID** and synchronize it with all contacts; have unique regID per contact book entry; exchange regIDs in safe place, so only A and B know about regIDa and regIDb.
- Set up *direct channel* before the DoS attack. The *direct channel* could be opened before any communication between users, in case if users are afraid of possible future DRP-level DoS attack, when they will not be able to send/receive *INVITE* packets.

### DRP-level DDoS attack on entire network

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

### Contact book sniffing by known regID

The attack is possible if an attacker runs a "sensor network" which records pairs of source and destination regIDs, to get track source and destination users, if he has a way to link regID to user. **Countermeasures:** frequently change regID and synchronize it with contacts; have unique regID per contact book entry; exchange regIDs in safe place, so only A and B know about regIDs; send many testINVITEs to some unknown regIDs (not in contact book) so it will be not possible to understand is it realINVITE or testINVITE.

### Target user IP sniffing by known regID

The attacker who is interested in getting IP address of target user and who knows his regIDa, can generate a regIDx in regIDspace that is close to target regIDa, become his neighbor and get IP address of the target user. Or the attacker can run his own rendezvous peer and track user's IP by regIDa. **Countermeasures:** use unique temporary regIDs, don't connect to any new neighbors after exposing the regIDa to some potentially bad users; use only trusted RPs

### UDP-level DDoS attacks

When an attacker knows IP address of target, he can run a regular UDP flooding attack over target IP. Cases:

- target IP address is rendezvous peer. **Resolutions:** use DDoS-resistant hosting providers to host RPs; dynamically change IP addresses of the RPs (exchange servers, deploy RP using automation); use CAPTCHA

- target IP address is user's peer. **Resolutions:** change internet service provider; see "Target user IP sniffing by known regID"

### UDP-level sniffers and blocks by ISP

An attacker or internet service provider is able to sniff IP traffic, intercept and manipulate DRP packets, block connections to peers. **Resolutions:** use VPN; run DRP peer at securely hosted server and access the server via RDP/SSH/VNC; encrypt DRP packets with some cipher (e.g. XOR, RC4, AES256) with secret keys; use random UDP port numbers; use another internet service provider; get list of accessible rendezvous peers from trusted contacts or from google/yandex search; scan random ip addresses and ports to find accessible rendezvous server; host public pages on github/facebook with list of accessible rendezvous peers.

### Stolen userKeypair

If an attacker gets access to user's device, he is able to copy his *userKeypair*. **Resolutions:** encrypt the userKeypair with a password; store *userKeypair* at hardware (cold wallet) or paper, in physically safe place(s).

### Lost userKeypair

An attacker can steal user's device (or user can lose/destroy the device himself) and lose access to the *userKeypair*. **Resolutions:** store *userKeypair* at hardware (cold wallet) or paper, in physically safe place(s). Multiple places could be used for redundancy.

### Stolen contact book

An attacker is able to copy user's contact book including names of contacts and their public keys, if contact book is stored at unsafe place.

### Stolen message history

**Resolutions:** do not store messages; encrypt messages with user's password.



































## References

Here is list of concepts, protocols, algorithms, used in the DRP.

- UDP protocol

- UDP hole punching
- SIP protocol
- tracert tool
- RTP protocol
- DHT
- Bittorrent, magnet links

- CPU-based Proof of Work, hashcash
- Double Ratchet Algorithm
- Bitcoin
- Elliptic Curves, Asymmetric Cryptography. Ed25519.

