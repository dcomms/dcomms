initial key exchange 

protocol, A: ID_B, B: doesn't know ID_A



1. A: Ra - random

   A -> B: C = Ra ^ PubA

2. B: Rb - random

   B -> A: C' = C ^ Rb

3. A -> B: C'' = C' ^ Ra

   B: PubA = C'' ^ Rb



(((Ra ^ PubA) ^ Rb) ^ Ra) ^ Rb ==  (Ra ^ Ra) ^ (Rb ^ Rb) ^ PubA == PubA



packets, application level

A <--> M <--> B

​     C           C1 - random

1. A: ike = { PubA: 256 bit, X (username): { length: 16 bit, name : length bits } }

   A -> B: ike1 = ike ^ Ra: 512 bi t

2. B -> A: ike2 = ike1 ^ Rb: 512 bit

3. A -> B: ike3 = ike2 ^ Ra: 512 bit

   B: ike = ike3 ^ Rb = { PubA: 256 bit, X }

   B: hash(PubA | X) == ID_A ---> to bob's contact book;  if ID-A is known , check it 



PubA ? Atmp

Atmp <--> D <---> C <--> B :   regID

userIdA ----direct udp channel----userB





