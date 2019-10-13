using Dcomms.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Dcomms.DRP
{
    /// <summary>
    /// = public key, point in regID space
    /// </summary>
    public class RegistrationId
    {
        //  byte Flags; // will include "type" = "ed25519 by default" // will include "type of distance metric"
        const byte FlagsMask_MustBeZero = 0b11110000;
        public byte[] Ed25519publicKey;
        public byte[] CachedEd25519publicKeySha256;
       
        public RegistrationId(byte[] ed25519publicKey)
        {
            Ed25519publicKey = ed25519publicKey;
        }

        public void Encode(BinaryWriter writer)
        {
            byte flags = 0;
            writer.Write(flags);
            if (Ed25519publicKey.Length != CryptoLibraries.Ed25519PublicKeySize) throw new ArgumentException();
            writer.Write(Ed25519publicKey);
        }
        public static RegistrationId Decode(BinaryReader reader)
        {
            var flags = reader.ReadByte();
            if ((flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            var r = new RegistrationId(reader.ReadBytes(CryptoLibraries.Ed25519PublicKeySize));        
            return r;
        }
        public override bool Equals(object obj)
        {
            var obj2 = (RegistrationId)obj;
            return MiscProcedures.EqualByteArrays(obj2.Ed25519publicKey, this.Ed25519publicKey);
        }
        public override int GetHashCode()
        {
            return MiscProcedures.GetArrayHashCode(Ed25519publicKey);
        }
        public override string ToString()
        {
            return MiscProcedures.ByteArrayToString(Ed25519publicKey);
        }
        public RegistrationIdDistance GetDistanceTo(ICryptoLibrary cryptoLibrary, RegistrationId another, int numberOfDimensions = 8) => new RegistrationIdDistance(cryptoLibrary, this, another, numberOfDimensions);
              
    }


    public class RegistrationIdDistance
    {

        double _distance_sumSqr; // 32 bytes of reg. public key: split into 16 dimensions of 2 bytes //   euclidean distance
        public unsafe RegistrationIdDistance(ICryptoLibrary cryptoLibrary, RegistrationId rpk1, RegistrationId rpk2, int numberOfDimensions = 8)
        {
            if (rpk1.CachedEd25519publicKeySha256 == null) rpk1.CachedEd25519publicKeySha256 = cryptoLibrary.GetHashSHA256(rpk1.Ed25519publicKey);
            var rpk1_ed25519publicKey_sha256 = rpk1.CachedEd25519publicKeySha256;
            if (rpk2.CachedEd25519publicKeySha256 == null) rpk2.CachedEd25519publicKeySha256 = cryptoLibrary.GetHashSHA256(rpk2.Ed25519publicKey);
            var rpk2_ed25519publicKey_sha256 = rpk2.CachedEd25519publicKeySha256;            

            if (rpk1_ed25519publicKey_sha256.Length != rpk2_ed25519publicKey_sha256.Length) throw new ArgumentException();
            _distance_sumSqr = 0;

            if (numberOfDimensions == 16)
            {
                fixed (byte* rpk1a = rpk1_ed25519publicKey_sha256, rpk2a = rpk2_ed25519publicKey_sha256)
                {
                    ushort* rpk1aPtr = (ushort*)rpk1a, rpk2aPtr = (ushort*)rpk2a;
                    int l = rpk1_ed25519publicKey_sha256.Length / 2;
                    for (int i = 0; i < l; i++, rpk1aPtr++, rpk2aPtr++)
                    {
                        var d_i = VectorComponentRoutine(*rpk1aPtr, *rpk2aPtr);
                        _distance_sumSqr += d_i * d_i;
                    }
                }
            }
            else if (numberOfDimensions == 8)
            {
                fixed (byte* rpk1a = rpk1_ed25519publicKey_sha256, rpk2a = rpk2_ed25519publicKey_sha256)
                {
                    uint* rpk1aPtr = (uint*)rpk1a, rpk2aPtr = (uint*)rpk2a;
                    int l = rpk1_ed25519publicKey_sha256.Length / 4;
                    for (int i = 0; i < l; i++, rpk1aPtr++, rpk2aPtr++)
                    {
                        var d_i = VectorComponentRoutine(*rpk1aPtr, *rpk2aPtr);
                        _distance_sumSqr += d_i * d_i;
                    }
                }
            }
            else if (numberOfDimensions == 4)
            {
                fixed (byte* rpk1a = rpk1_ed25519publicKey_sha256, rpk2a = rpk2_ed25519publicKey_sha256)
                {
                    ulong* rpk1aPtr = (ulong*)rpk1a, rpk2aPtr = (ulong*)rpk2a;
                    int l = rpk1_ed25519publicKey_sha256.Length / 8;
                    for (int i = 0; i < l; i++, rpk1aPtr++, rpk2aPtr++)
                    {
                        var d_i = VectorComponentRoutine(*rpk1aPtr, *rpk2aPtr);
                        _distance_sumSqr += d_i * d_i;
                    }
                }
            }
            else if (numberOfDimensions == 2)
            {
                GetVectorValues_2(rpk1_ed25519publicKey_sha256, out var v1_0, out var v1_1);
                GetVectorValues_2(rpk2_ed25519publicKey_sha256, out var v2_0, out var v2_1);

                var d_0 = VectorComponentRoutine(v1_0, v2_0);
                var d_1 = VectorComponentRoutine(v1_1, v2_1);
                _distance_sumSqr += d_0 * d_0;
                _distance_sumSqr += d_1 * d_1;
            }
            else throw new NotImplementedException();
        }
        static readonly BigInteger GetVectorValues_2_BigInteger_MaxValue = BigInteger.Pow(2, 16*8-1);
        static void GetVectorValues_2(byte[] rid_ed25519publicKey_sha256, out double v0, out double v1)
        {
            var a0 = new byte[16]; for (int i = 0; i < 16; i++) a0[i] = rid_ed25519publicKey_sha256[i];
            var a1 = new byte[16]; for (int i = 0; i < 16; i++) a1[i] = rid_ed25519publicKey_sha256[16+i];
            // 16+16 bytes 

            var bi0 = new BigInteger(a0); if (bi0.Sign < 0) bi0 = -bi0;
            var bi1 = new BigInteger(a1); if (bi1.Sign < 0) bi1 = -bi1;


            v0 = (double)bi0 / (double)GetVectorValues_2_BigInteger_MaxValue;
            v1 = (double)bi1 / (double)GetVectorValues_2_BigInteger_MaxValue;
        }
        public static unsafe double[] GetVectorValues(ICryptoLibrary cryptoLibrary, RegistrationId rid, int numberOfDimensions = 8)
        {
            var r = new double[numberOfDimensions];
            
            if (rid.CachedEd25519publicKeySha256 == null) rid.CachedEd25519publicKeySha256 = cryptoLibrary.GetHashSHA256(rid.Ed25519publicKey);
            var rid_ed25519publicKey_sha256 = rid.CachedEd25519publicKeySha256;

            if (numberOfDimensions == 16)
            {
                fixed (byte* rpk1a = rid_ed25519publicKey_sha256)
                {
                    ushort* rpk1aPtr = (ushort*)rpk1a;
                    int l = rid_ed25519publicKey_sha256.Length / 2;
                    for (int i = 0; i < l; i++, rpk1aPtr++)
                    {
                        r[i] = (double)(*rpk1aPtr) / UInt16.MaxValue;
                    }
                }
            }
            else if (numberOfDimensions == 8)
            {
                fixed (byte* rpk1a = rid_ed25519publicKey_sha256)
                {
                    uint* rpk1aPtr = (uint*)rpk1a;
                    int l = rid_ed25519publicKey_sha256.Length / 4;
                    for (int i = 0; i < l; i++, rpk1aPtr++)
                    {
                        r[i] = (double)(*rpk1aPtr) / UInt32.MaxValue;
                    }
                }
            }
            else if (numberOfDimensions == 4)
            {
                fixed (byte* rpk1a = rid_ed25519publicKey_sha256)
                {
                    ulong* rpk1aPtr = (ulong*)rpk1a;
                    int l = rid_ed25519publicKey_sha256.Length / 8;
                    for (int i = 0; i < l; i++, rpk1aPtr++)
                    {
                        r[i] = (double)(*rpk1aPtr) / ulong.MaxValue;
                    }
                }
            }
            else if (numberOfDimensions == 2)
            {
                GetVectorValues_2(rid_ed25519publicKey_sha256, out var v0, out var v1);
                r[0] = v0;
                r[1] = v1;
            }
            else throw new NotImplementedException();

            return r;
        }
        public static double VectorComponentRoutine(ushort vector1_i, ushort vector2_i)
        {
            int r;
            if (vector2_i > vector1_i) r = vector2_i - vector1_i;
            else r = vector1_i - vector2_i;
            if (r > 32768) r = 65536 - r;
            return r;

            /*
                   r1_i    r2_i   distance_i
                  32000   32001     1
                 -32000  -32001     1
                 -32767   32768     1
                  32767  -32767     2
             */
        }
        public static double VectorComponentRoutine(double vector1_i, double vector2_i)
        {
            double r;
            if (vector2_i > vector1_i) r = vector2_i - vector1_i;
            else r = vector1_i - vector2_i;
            if (r > 0.5) r = 1.0 - r;
            return r;
        }

        public static double VectorComponentRoutine(uint vector1_i, uint vector2_i)
        {
            double r;
            if (vector2_i > vector1_i) r = (double)vector2_i - (double)vector1_i;
            else r = (double)vector1_i - (double)vector2_i;
            if (r > (double)Int32.MaxValue) r = (double)UInt32.MaxValue - r;
            return r;
        }
        public static double VectorComponentRoutine(ulong vector1_i, ulong vector2_i)
        {
            double r;
            if (vector2_i > vector1_i) r = (double)vector2_i - (double)vector1_i;
            else r = (double)vector1_i - (double)vector2_i;
            if (r > (double)long.MaxValue) r = (double)ulong.MaxValue - r;
            return r;
        }

        public bool IsGreaterThan(RegistrationIdDistance another)
        {
            return this._distance_sumSqr > another._distance_sumSqr;
        }
        public bool IsLessThan(uint another)
        {
            return this._distance_sumSqr < another * another;
        }

        public override string ToString() => ((float)_distance_sumSqr).ToString("E02");
        public double ToDouble() => Math.Sqrt(_distance_sumSqr);
         
        public static void ProcessVectorInLoopedRegistrationIdSpace(double from, ref double to)
        {
            if (to - from > 0.5)
            { // case 1
                to -= 1.0;
            }
            else
            {
                if (from - to > 0.5)
                { // case 2
                    to += 1.0;
                }
            }
        }
    }

    public class VectorSectorIndexCalculator
    {
        readonly List<double[]> _simplexVertices;
        public int IndexesCount => _simplexVertices.Count;
        public VectorSectorIndexCalculator(int numberOfDimensions)
        {
            if (numberOfDimensions == 2)
            {
                _simplexVertices = new List<double[]>();
                _simplexVertices.Add(new double[] { 0, 0 });
                _simplexVertices.Add(new double[] { 1, 0 });
                _simplexVertices.Add(new double[] { 0.5, Math.Sqrt(3) * 0.5 });
            }
            else if (numberOfDimensions == 4)
            { // https://en.wikipedia.org/wiki/5-cell
                _simplexVertices = new List<double[]>();
                _simplexVertices.Add(new double[] { 1, 1, 1, -1 / Math.Sqrt(5) });
                _simplexVertices.Add(new double[] { 1, -1, -1, -1 / Math.Sqrt(5) });
                _simplexVertices.Add(new double[] { -1, 1, -1, -1 / Math.Sqrt(5) });
                _simplexVertices.Add(new double[] { -1, -1, 1, -1 / Math.Sqrt(5) });
                _simplexVertices.Add(new double[] { 0, 0, 0, Math.Sqrt(5) - 1 / Math.Sqrt(5) });
            }
            else if (numberOfDimensions == 8)
            { // https://en.wikipedia.org/wiki/8-simplex
                _simplexVertices = new List<double[]>();
                _simplexVertices.Add(new double[] { 1.0 / 6, Math.Sqrt(1.0 / 28), Math.Sqrt(1.0 / 21), Math.Sqrt(1.0 / 15), Math.Sqrt(1.0 / 10), Math.Sqrt(1.0 / 6), Math.Sqrt(1.0 / 3), 1 });
                _simplexVertices.Add(new double[] { 1.0 / 6, Math.Sqrt(1.0 / 28), Math.Sqrt(1.0 / 21), Math.Sqrt(1.0 / 15), Math.Sqrt(1.0 / 10), Math.Sqrt(1.0 / 6), -2.0 * Math.Sqrt(1.0 / 3), 0 });
                _simplexVertices.Add(new double[] { 1.0 / 6, Math.Sqrt(1.0 / 28), Math.Sqrt(1.0 / 21), Math.Sqrt(1.0 / 15), Math.Sqrt(1.0 / 10), -Math.Sqrt(3.0 / 2), 0, 0 });
                _simplexVertices.Add(new double[] { 1.0 / 6, Math.Sqrt(1.0 / 28), Math.Sqrt(1.0 / 21), Math.Sqrt(1.0 / 15), -2.0 * Math.Sqrt(2.0 / 5), 0, 0, 0 });
                _simplexVertices.Add(new double[] { 1.0 / 6, Math.Sqrt(1.0 / 28), Math.Sqrt(1.0 / 21), -Math.Sqrt(5.0 / 3), 0, 0, 0, 0 });
                _simplexVertices.Add(new double[] { 1.0 / 6, Math.Sqrt(1.0 / 28), -Math.Sqrt(12.0 / 7), 0, 0, 0, 0, 0 });
                _simplexVertices.Add(new double[] { 1.0 / 6, -Math.Sqrt(7.0 / 4), 0, 0, 0, 0, 0, 0 });
                _simplexVertices.Add(new double[] { -4.0 / 3, 0, 0, 0, 0, 0, 0, 0 });
            }
            else throw new NotImplementedException();
            
            for (int i = 0; i < numberOfDimensions; i++)
            {
                double avg = 0;
                foreach (var simplexVertex in _simplexVertices) avg += simplexVertex[i];
                avg /= _simplexVertices.Count;
                foreach (var simplexVertex in _simplexVertices) simplexVertex[i] -= avg;
            }
        }
        public int GetSectorIndex(double[] vectorFromLocalPeerToNeighbor)
        {
            double? maxMultResult = null;
            int? bextVertexIndex = null;
            for (int simplexVertexIndex = 0; simplexVertexIndex < _simplexVertices.Count; simplexVertexIndex++)
            {
                var simplexVertex = _simplexVertices[simplexVertexIndex];

                    double multResult = 0;
                for (int i = 0; i < vectorFromLocalPeerToNeighbor.Length; i++)
                    multResult += simplexVertex[i] * vectorFromLocalPeerToNeighbor[i];
                if (maxMultResult == null || multResult > maxMultResult.Value)
                {
                    bextVertexIndex = simplexVertexIndex;
                    maxMultResult = multResult;
                }
            }
            return bextVertexIndex.Value;
        }
    }

    public class P2pConnectionValueCalculator
    {
        ICryptoLibrary _cryptoLibrary;
        int _numberOfDimensions;
        double[] _localPeerVector;
      //  double[] _vectorFromLocalPeerToAverageNeighborNormalized; // null if no neighbors
        internal string Description
        {
            get
            {
                var sb = new StringBuilder("{");
             //   foreach (var d in _vectorFromLocalPeerToAverageNeighborNormalized)
            //        sb.AppendFormat("{0:0.00};", d);
                sb.Append("}");
                return sb.ToString();
            }
        }

        int[] _currentNeighborsCountPerSectors; 
        //int GetSectorIndex(double[] vectorFromLocalPeerToNeighbor)
        //{

        //    if (_numberOfDimensions == 2)
        //    {
        //        if (vectorFromLocalPeerToNeighbor[0] > 0)
        //        {
        //            if (vectorFromLocalPeerToNeighbor[1] > 0) return 0;
        //            else return 1;
        //        }
        //        else
        //        {
        //            if (vectorFromLocalPeerToNeighbor[1] > 0) return 2;
        //            else return 3;
        //        }
        //    }
        //    else throw new NotImplementedException();
        //}
        readonly VectorSectorIndexCalculator _vsic;

        public P2pConnectionValueCalculator(RegistrationId localPeer, ICryptoLibrary cryptoLibrary, int numberOfDimensions, IEnumerable<RegistrationId> currentNeighbors)
        {
            _cryptoLibrary = cryptoLibrary;
            _numberOfDimensions = numberOfDimensions;
            _vsic = new VectorSectorIndexCalculator(_numberOfDimensions);
            _currentNeighborsCountPerSectors = new int[_vsic.IndexesCount];            
            _localPeerVector = RegistrationIdDistance.GetVectorValues(_cryptoLibrary, localPeer, _numberOfDimensions);
                      
            foreach (var neighborVector in currentNeighbors.Select(x => RegistrationIdDistance.GetVectorValues(cryptoLibrary, x, numberOfDimensions)))
            {
                var vectorFromLocalPeerToNeighbor = new double[numberOfDimensions];                  
                for (int i = 0; i < numberOfDimensions; i++)
                {
                    var from = _localPeerVector[i];
                    var to = neighborVector[i];
                    RegistrationIdDistance.ProcessVectorInLoopedRegistrationIdSpace(from, ref to);
                    vectorFromLocalPeerToNeighbor[i] = to - from;
                }
                _currentNeighborsCountPerSectors[_vsic.GetSectorIndex(vectorFromLocalPeerToNeighbor)]++;
            }            
           

            //if (currentNeighbors.Any())
            //{
            //    var neighborsVectors = currentNeighbors.Select(x => RegistrationIdDistance.GetVectorValues(cryptoLibrary, x, numberOfDimensions)).ToList();

            //    var intermediate1 = new double[numberOfDimensions];
            //    foreach (var neighborVector in neighborsVectors)
            //        for (int i = 0; i < numberOfDimensions; i++)
            //        {
            //            var from = _localPeerVector[i];
            //            var to = neighborVector[i];
            //            RegistrationIdDistance.ProcessVectorInLoopedRegistrationIdSpace(from, ref to);
            //            intermediate1[i] += to - from;
            //        }
            //    double intermediate1L = 0;
            //    for (int i = 0; i < numberOfDimensions; i++)
            //        intermediate1L += intermediate1[i] * intermediate1[i];
            //    intermediate1L = Math.Sqrt(intermediate1L);

            //    _vectorFromLocalPeerToAverageNeighborNormalized = new double[numberOfDimensions];
            //    for (int i = 0; i < numberOfDimensions; i++)
            //        _vectorFromLocalPeerToAverageNeighborNormalized[i] = intermediate1[i] / intermediate1L;
            //}
        }
        public double GetValue(RegistrationId neighbor)
        {
            var neighborVector = RegistrationIdDistance.GetVectorValues(_cryptoLibrary, neighbor, _numberOfDimensions);
                      
            double distanceFromLocalPeerToNeighbor = 0;
            var vectorFromLocalPeerToNeighbor = new double[_numberOfDimensions];
          //  double cosAngle = 0; // scalar multiplication   of (vector from local peer to the neighbor) by (vector from local peer to average neighbor  (normalized))
            for (int i = 0; i < neighborVector.Length; i++)
            {
                var from = _localPeerVector[i];
                var to = neighborVector[i];
                RegistrationIdDistance.ProcessVectorInLoopedRegistrationIdSpace(from, ref to);
                var vectorFromLocalPeerToNeighbor_i = to - from;
                vectorFromLocalPeerToNeighbor[i] = vectorFromLocalPeerToNeighbor_i;
                distanceFromLocalPeerToNeighbor += vectorFromLocalPeerToNeighbor_i * vectorFromLocalPeerToNeighbor_i;

              //  if (_vectorFromLocalPeerToAverageNeighborNormalized != null)
             //   {
             //       cosAngle += vectorFromLocalPeerToNeighbor_i * (- _vectorFromLocalPeerToAverageNeighborNormalized[i]);
            //    }
            }
            distanceFromLocalPeerToNeighbor = Math.Sqrt(distanceFromLocalPeerToNeighbor);

            double r = -distanceFromLocalPeerToNeighbor;

            var sectorIndex = _vsic.GetSectorIndex(vectorFromLocalPeerToNeighbor);
            if (_currentNeighborsCountPerSectors[sectorIndex] == 0) r += 10.0;
            else if (_currentNeighborsCountPerSectors[sectorIndex] == 1) r += 1.0;

            //  if (cosAngle < 0)
            //      return -1 - distanceFromLocalPeerToNeighbor;

            return r;
        }
    }


    public class RegistrationPrivateKey
    {
        public byte[] ed25519privateKey;
    }
    public class RegistrationSignature
    {
        byte Flags; // will include "type" = "ed25519 by default"
        const byte FlagsMask_MustBeZero = 0b11110000;
        public byte[] ed25519signature;

        public static RegistrationSignature Sign(ICryptoLibrary cryptoLibrary, Action<BinaryWriter> writeSignedFields, RegistrationPrivateKey privateKey)
        {
            var r = new RegistrationSignature();
            var ms = new MemoryStream(); using (var writer = new BinaryWriter(ms)) writeSignedFields(writer);
            r.ed25519signature = cryptoLibrary.SignEd25519(
                    ms.ToArray(),
                    privateKey.ed25519privateKey);
            return r;
        }


        public void Encode(BinaryWriter writer)
        {
            writer.Write(Flags);
            if (ed25519signature.Length != CryptoLibraries.Ed25519SignatureSize) throw new ArgumentException();
            writer.Write(ed25519signature);
        }
        public static RegistrationSignature Decode(BinaryReader reader)
        {
            var r = new RegistrationSignature();
            r.Flags = reader.ReadByte();
            if ((r.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            r.ed25519signature = reader.ReadBytes(CryptoLibraries.Ed25519SignatureSize);
            return r;
        }
        public static RegistrationSignature DecodeAndVerify(BinaryReader reader, ICryptoLibrary cryptoLibrary, Action<BinaryWriter> writeSignedFields, RegistrationId publicKey)
        {
            var r = Decode(reader);  
            if (!r.Verify(cryptoLibrary, writeSignedFields, publicKey)) throw new BadSignatureException();     
            return r;
        }
        public bool Verify(ICryptoLibrary cryptoLibrary, Action<BinaryWriter> writeSignedFields, RegistrationId publicKey)
        {
            var signedData = new MemoryStream();
            using (var writer = new BinaryWriter(signedData))
                writeSignedFields(writer);
            if (cryptoLibrary.VerifyEd25519(signedData.ToArray(), ed25519signature, publicKey.Ed25519publicKey) == false)
                return false;
            return true;
        }
    }
    public class EcdhPublicKey
    {
        byte Flags; // will include "type" = "ec25519 ecdh" by default
        const byte FlagsMask_MustBeZero = 0b11110000;
       
        public byte[] Ecdh25519PublicKey;
        public EcdhPublicKey()
        {
        }
        public EcdhPublicKey(byte[] ecdh25519PublicKey)
        {
            Ecdh25519PublicKey = ecdh25519PublicKey;
        }
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Flags);
            if (Ecdh25519PublicKey.Length != CryptoLibraries.Ecdh25519PublicKeySize) throw new ArgumentException();
            writer.Write(Ecdh25519PublicKey);
        }
        public static EcdhPublicKey Decode(BinaryReader reader)
        {
            var r = new EcdhPublicKey();
            r.Flags = reader.ReadByte();
            if ((r.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            r.Ecdh25519PublicKey = reader.ReadBytes(CryptoLibraries.Ecdh25519PublicKeySize);
            // todo: check if it is valid point on curve  - do we really need to check it?
            return r;
        }
    }
  
    public class HMAC
    {
        byte Flags; // will include "type" = "ecdhe->KDF->sharedkey -> +plainText -> sha256" by default
        const byte FlagsMask_MustBeZero = 0b11110000;
        public byte[] hmacSha256; // 32 bytes for hmac_sha256
        public void Encode(BinaryWriter writer)
        {
            writer.Write(Flags);
            if (hmacSha256.Length != 32) throw new ArgumentException();
            writer.Write(hmacSha256);
        }

        public static HMAC Decode(BinaryReader reader)
        {
            var r = new HMAC();
            r.Flags = reader.ReadByte();
            if ((r.Flags & FlagsMask_MustBeZero) != 0) throw new NotImplementedException();
            r.hmacSha256 = reader.ReadBytes(32);
            return r;
        }
        public override bool Equals(object obj)
        {
            var obj2 = (HMAC)obj;
            if (obj2.Flags != this.Flags) return false;
            return MiscProcedures.EqualByteArrays(obj2.hmacSha256, this.hmacSha256);
        }
        public override string ToString() => MiscProcedures.ByteArrayToString(hmacSha256);
    }
}
