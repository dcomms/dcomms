# Decentralized Routing Protocol

## Abstract

The Decentralized Routing Protocol (DRP) is designed to build peer-to-peer (P2P) networks where peers can contact each other without servers. The DRP runs over UDP protocol and uses technique of "UDP hole punching" for NAT/firewall traversal. DRP is used to initialize direct UDP channel between two peers, it does not carry communications between peers. The DRP is designed to build secure decentralized messengers,  UC apps.

## Concepts

### User's identifiers. ID, PublicKey, PrivateKey

**user**: a person who uses the messenger.

**keypair**: {PublicKey, PrivateKey} is used as a self-signed certificate of a *user*. Private key is used to sign, public key - to verify packets. We select Ed25519 ECDSA for the asymmetric crypography.

**idNonce**: random bytes (salt), is used to change user's ID without changing his public key

**ID**=SHA512(*PublicKey*|*idNonce*), 512 bits

ID is used to deliver packets across the P2P network, to sign transmitted packets, verify the packets.

### ID space, distance, vectors

The ID is splitted into groups of bits, each group indicates a coordinate in 8-D **IDspace**. **Distance** between two IDs is defined as Euclidean distance. **Vector** from IDa to IDb is defined in same way as in Euclidean geometry.

### Neighborhood and routing

**Interconnection** between **neighbour** peers A and N is a direct, bidirectional UDP channel, used to transfer DRP packets. Peer A is interested to interconnect with peers who are close to its own ID. 

Routing at peer X of packet to towards  is proxying

### Contact book entries

B=Bob=some other user

contact_book_entry = { PublicKeyB, idNonceB }

## Packets (Abstract)

SettlementRequest, SettelementResponse

InitialLookup, Response



## Stages

### Settlement. Rendezvous Nodes.

Rendezvous Node -> Users at far location -> Users at close location (neighbours).

UDP hole punching technique.

### Testing neighbours

Every peer is interested to measure quality of his neighbous. He sends test messages to some (random) destinations and measures packet loss and round-trip time (RTT).

### Initial Lookup

from A to B, to add to contact book.

### Subsequent Lookup

lookup with preshared key in the contact book

### Direct UDP channel

When both users A and B agree to set up **direct channel**, they disclose IP addresses to each other and start end-to-end encrypted communication over some other protocol (DTLS, SIP, RTP).

The direct channel could be opened before any communication between users, in case if users are afraid of possible future DRP-level DoS attack, when they will not be able to set up the direct channel.

## Attacks and countermeasures

### DRP-level DDoS attacks

DDoS attack on target user A is possible when an attacker owns a botnet and knows ID of user A. He is able to register many fake users X talking to each other normally, then at some point send oackets towards A, in this way flooding *IDspace* near location of A.

### Contact book sniffing

The attack ould be run if an attacker runs a "sensor network" which records pairs of source and destination IDs.

### UDP-level DDoS attacks

When an attacker knows IP address of target, he can run a regular UDP flooding attack over target IP.

## Packets (Protocol Specification)

SettlementRequest: {IDa, }