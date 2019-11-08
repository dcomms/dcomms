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
        public RegistrationIdDistance GetDistanceTo(ICryptoLibrary cryptoLibrary, RegistrationId another, int numberOfDimensions) => new RegistrationIdDistance(cryptoLibrary, this, another, numberOfDimensions);
        public static double[] GetDifferenceVector(RegistrationId from, RegistrationId to, ICryptoLibrary cryptoLibrary, int numberOfDimensions)
        {
            var fromRegIdVector = RegistrationIdDistance.GetVectorValues(cryptoLibrary, from, numberOfDimensions);
            var destinationRegIdVector = RegistrationIdDistance.GetVectorValues(cryptoLibrary, to, numberOfDimensions);
            var diff = new double[fromRegIdVector.Length];
            for (int i = 0; i < diff.Length; i++)
                diff[i] = RegistrationIdDistance.GetDifferenceInLoopedRegistrationIdSpace(fromRegIdVector[i], destinationRegIdVector[i]);
            return diff;
        }
        public static float[] GetDifferenceVectorF(RegistrationId from, RegistrationId to, ICryptoLibrary cryptoLibrary, int numberOfDimensions)
            => GetDifferenceVector(from, to, cryptoLibrary, numberOfDimensions).Select(x => (float)x).ToArray();
    }


    public class RegistrationIdDistance
    {
        double _distance_sumSqr; // 32 bytes of reg. public key: split into 16 dimensions of 2 bytes //   euclidean distance
        public unsafe RegistrationIdDistance(ICryptoLibrary cryptoLibrary, RegistrationId rpk1, RegistrationId rpk2, int numberOfDimensions)
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
        static void GetVectorValues_2(byte[] rid_ed25519publicKey_sha256, out float v0, out float v1)
        {
            var a0 = new byte[16]; for (int i = 0; i < 16; i++) a0[i] = rid_ed25519publicKey_sha256[i];
            var a1 = new byte[16]; for (int i = 0; i < 16; i++) a1[i] = rid_ed25519publicKey_sha256[16+i];
            // 16+16 bytes 

            var bi0 = new BigInteger(a0); if (bi0.Sign < 0) bi0 = -bi0;
            var bi1 = new BigInteger(a1); if (bi1.Sign < 0) bi1 = -bi1;


            v0 = (float)bi0 / (float)GetVectorValues_2_BigInteger_MaxValue;
            v1 = (float)bi1 / (float)GetVectorValues_2_BigInteger_MaxValue;
        }
        public static unsafe double[] GetVectorValues(ICryptoLibrary cryptoLibrary, RegistrationId rid, int numberOfDimensions)
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

        public static double GetDifferenceInLoopedRegistrationIdSpace(double from, double to)
        {
            ProcessVectorInLoopedRegistrationIdSpace(from, ref to);
            return to - from;
        }
        static void ProcessVectorInLoopedRegistrationIdSpace(double from, ref double to)
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
        public static float GetDifferenceInLoopedRegistrationIdSpace(float from, float to)
        {
            ProcessVectorInLoopedRegistrationIdSpace(from, ref to);
            return to - from;
        }
        public static void ProcessVectorInLoopedRegistrationIdSpace(float from, ref float to)
        {
            if (to - from > 0.5f)
            { // case 1
                to -= 1.0f;
            }
            else
            {
                if (from - to > 0.5f)
                { // case 2
                    to += 1.0f;
                }
            }
        }
    }

    public class VectorSectorIndexCalculator
    {
        readonly List<float[]> _simplexVertices;
        public int IndexesCount => _simplexVertices.Count;
        readonly int _numberOfDimensions;
        public VectorSectorIndexCalculator(int numberOfDimensions)
        {
            _numberOfDimensions = numberOfDimensions;
            if (numberOfDimensions == 2)
            {
                _simplexVertices = new List<float[]>();
                _simplexVertices.Add(new float[] { 0, 0 });
                _simplexVertices.Add(new float[] { 1, 0 });
                _simplexVertices.Add(new float[] { 0.5f, (float)Math.Sqrt(3) * 0.5f });
            }
            else if (numberOfDimensions == 4)
            { // https://en.wikipedia.org/wiki/5-cell
                _simplexVertices = new List<float[]>();
                _simplexVertices.Add(new float[] { 1, 1, 1, -1 / (float)Math.Sqrt(5) });
                _simplexVertices.Add(new float[] { 1, -1, -1, -1 / (float)Math.Sqrt(5) });
                _simplexVertices.Add(new float[] { -1, 1, -1, -1 / (float)Math.Sqrt(5) });
                _simplexVertices.Add(new float[] { -1, -1, 1, -1 / (float)Math.Sqrt(5) });
                _simplexVertices.Add(new float[] { 0, 0, 0, (float)Math.Sqrt(5) - 1 / (float)Math.Sqrt(5) });
            }
            else if (numberOfDimensions == 8)
            { // https://en.wikipedia.org/wiki/8-simplex
                _simplexVertices = new List<float[]>();
                _simplexVertices.Add(new float[] { 1.0f / 6, (float)Math.Sqrt(1.0f / 28), (float)Math.Sqrt(1.0f / 21), (float)Math.Sqrt(1.0f / 15), (float)Math.Sqrt(1.0f / 10), (float)Math.Sqrt(1.0f / 6), (float)Math.Sqrt(1.0f / 3), 1 });
                _simplexVertices.Add(new float[] { 1.0f / 6, (float)Math.Sqrt(1.0f / 28), (float)Math.Sqrt(1.0f / 21), (float)Math.Sqrt(1.0f / 15), (float)Math.Sqrt(1.0f / 10), (float)Math.Sqrt(1.0f / 6), -2.0f * (float)Math.Sqrt(1.0f / 3), 0 });
                _simplexVertices.Add(new float[] { 1.0f / 6, (float)Math.Sqrt(1.0f / 28), (float)Math.Sqrt(1.0f / 21), (float)Math.Sqrt(1.0f / 15), (float)Math.Sqrt(1.0f / 10), -(float)Math.Sqrt(3.0f / 2), 0, 0 });
                _simplexVertices.Add(new float[] { 1.0f / 6, (float)Math.Sqrt(1.0f / 28), (float)Math.Sqrt(1.0f / 21), (float)Math.Sqrt(1.0f / 15), -2.0f * (float)Math.Sqrt(2.0f / 5), 0, 0, 0 });
                _simplexVertices.Add(new float[] { 1.0f / 6, (float)Math.Sqrt(1.0f / 28), (float)Math.Sqrt(1.0f / 21), -(float)Math.Sqrt(5.0f / 3), 0, 0, 0, 0 });
                _simplexVertices.Add(new float[] { 1.0f / 6, (float)Math.Sqrt(1.0f / 28), -(float)Math.Sqrt(12.0f / 7), 0, 0, 0, 0, 0 });
                _simplexVertices.Add(new float[] { 1.0f / 6, -(float)Math.Sqrt(7.0f / 4), 0, 0, 0, 0, 0, 0 });
                _simplexVertices.Add(new float[] { -4.0f / 3, 0, 0, 0, 0, 0, 0, 0 });
                _simplexVertices.Add(new float[] { 0, 0, 0, 0, 0, 0, 0, 1 }); // ????????????????????
            }
            else throw new NotImplementedException();
            
            for (int i = 0; i < numberOfDimensions; i++)
            {
                float avg = 0;
                foreach (var simplexVertex in _simplexVertices) avg += simplexVertex[i];
                avg /= _simplexVertices.Count;
                foreach (var simplexVertex in _simplexVertices) simplexVertex[i] -= avg;
            }
        }
        public float[] GetSimplexVector(int sectorIndex) => _simplexVertices[sectorIndex];
        public int GetSectorIndex(float[] vectorFromLocalPeerToNeighbor)
        {
            float? maxMultResult = null;
            int? bextVertexIndex = null;
            for (int simplexVertexIndex = 0; simplexVertexIndex < _simplexVertices.Count; simplexVertexIndex++)
            {
                var simplexVertex = _simplexVertices[simplexVertexIndex];

                float multResult = 0;
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
        

        IEnumerable<byte[]> GetGroupsOfSimplexVertices(int simplexesCountInGroup)
        {
            var r = new byte[simplexesCountInGroup];
            // enumerate all combinations/groups of vertex indexes
            //  for (byte x = 0; x < )
            if (IndexesCount == 3)
            {
                if (simplexesCountInGroup == 1)
                {
                    r[0] = 0; yield return r;
                    r[0] = 1; yield return r;
                    r[0] = 2; yield return r;
                }
                else if (simplexesCountInGroup == 2)
                {
                    r[0] = 0; r[1] = 1; yield return r;
                    r[0] = 0; r[1] = 2; yield return r;
                    r[0] = 1; r[1] = 2; yield return r;
                }
                else throw new NotImplementedException();
            }
            else if (IndexesCount == 9)
            {
                if (simplexesCountInGroup == 1)
                {
                    for (byte i = 0; i < IndexesCount; i++)
                    {
                        r[0] = i;
                        yield return r;
                    }
                }
                else if (simplexesCountInGroup == 2)
                {
                    for (byte i0 = 0; i0 < IndexesCount; i0++)
                    {
                        for (byte i1 = (byte)(i0 + 1); i1 < IndexesCount; i1++)
                        {
                            r[0] = i0;
                            r[1] = i1;
                            yield return r;
                        }
                    }
                }
                else if (simplexesCountInGroup == 3)
                {
                    for (byte i0 = 0; i0 < IndexesCount; i0++)
                        for (byte i1 = (byte)(i0 + 1); i1 < IndexesCount; i1++)
                            for (byte i2 = (byte)(i1 + 1); i2 < IndexesCount; i2++)
                            {
                                r[0] = i0;
                                r[1] = i1;
                                r[2] = i2;
                                yield return r;
                            }
                }
                else if (simplexesCountInGroup == 4)
                {
                    for (byte i0 = 0; i0 < IndexesCount; i0++)
                        for (byte i1 = (byte)(i0 + 1); i1 < IndexesCount; i1++)
                            for (byte i2 = (byte)(i1 + 1); i2 < IndexesCount; i2++)
                                for (byte i3 = (byte)(i2 + 1); i3 < IndexesCount; i3++)
                                {
                                    r[0] = i0; r[1] = i1; r[2] = i2; r[3] = i3;
                                    yield return r;
                                }
                }
                else if (simplexesCountInGroup == 5)
                {
                    for (byte i0 = 0; i0 < IndexesCount; i0++)
                        for (byte i1 = (byte)(i0 + 1); i1 < IndexesCount; i1++)
                            for (byte i2 = (byte)(i1 + 1); i2 < IndexesCount; i2++)
                                for (byte i3 = (byte)(i2 + 1); i3 < IndexesCount; i3++)
                                    for (byte i4 = (byte)(i3 + 1); i4 < IndexesCount; i4++)
                                    {
                                        r[0] = i0; r[1] = i1; r[2] = i2; r[3] = i3; r[4] = i4;
                                        yield return r;
                                    }
                }
                else if (simplexesCountInGroup == 6)
                {
                    for (byte i0 = 0; i0 < IndexesCount; i0++)
                        for (byte i1 = (byte)(i0 + 1); i1 < IndexesCount; i1++)
                            for (byte i2 = (byte)(i1 + 1); i2 < IndexesCount; i2++)
                                for (byte i3 = (byte)(i2 + 1); i3 < IndexesCount; i3++)
                                    for (byte i4 = (byte)(i3 + 1); i4 < IndexesCount; i4++)
                                        for (byte i5 = (byte)(i4 + 1); i5 < IndexesCount; i5++)
                                        {
                                            r[0] = i0; r[1] = i1; r[2] = i2; r[3] = i3; r[4] = i4; r[5] = i5;
                                            yield return r;
                                        }
                }
                else if (simplexesCountInGroup == 7)
                {
                    for (byte i0 = 0; i0 < IndexesCount; i0++)
                        for (byte i1 = (byte)(i0 + 1); i1 < IndexesCount; i1++)
                            for (byte i2 = (byte)(i1 + 1); i2 < IndexesCount; i2++)
                                for (byte i3 = (byte)(i2 + 1); i3 < IndexesCount; i3++)
                                    for (byte i4 = (byte)(i3 + 1); i4 < IndexesCount; i4++)
                                        for (byte i5 = (byte)(i4 + 1); i5 < IndexesCount; i5++)
                                            for (byte i6 = (byte)(i5 + 1); i6 < IndexesCount; i6++)
                                            {
                                                r[0] = i0; r[1] = i1; r[2] = i2; r[3] = i3; r[4] = i4; r[5] = i5; r[6] = i6;
                                                yield return r;
                                            }
                }
                else if (simplexesCountInGroup == 8)
                {
                    for (byte i0 = 0; i0 < IndexesCount; i0++)
                        for (byte i1 = (byte)(i0 + 1); i1 < IndexesCount; i1++)
                            for (byte i2 = (byte)(i1 + 1); i2 < IndexesCount; i2++)
                                for (byte i3 = (byte)(i2 + 1); i3 < IndexesCount; i3++)
                                    for (byte i4 = (byte)(i3 + 1); i4 < IndexesCount; i4++)
                                        for (byte i5 = (byte)(i4 + 1); i5 < IndexesCount; i5++)
                                            for (byte i6 = (byte)(i5 + 1); i6 < IndexesCount; i6++)
                                                for (byte i7 = (byte)(i6 + 1); i7 < IndexesCount; i7++)
                                                {
                                                    r[0] = i0; r[1] = i1; r[2] = i2; r[3] = i3; r[4] = i4; r[5] = i5; r[6] = i6; r[7] = i7;
                                                    yield return r;
                                                }
                }
                else throw new NotImplementedException();
            }
            else throw new NotImplementedException(); // todo 8D

        }
        internal IEnumerable<double[]> EnumerateDirections()
        {
            for (int simplexesCountInGroup = 1; simplexesCountInGroup <= _numberOfDimensions; simplexesCountInGroup++)
            {
                // find all groups of simplex vertices
                foreach (var groupOfSimplexes in GetGroupsOfSimplexVertices(simplexesCountInGroup))
                {
                    var groupAverageVector = new double[_numberOfDimensions];
                    for (int simplexIndexInGroup = 0; simplexIndexInGroup < simplexesCountInGroup; simplexIndexInGroup++)
                    {
                        var simplexVertex = GetSimplexVector(groupOfSimplexes[simplexIndexInGroup]);
                        for (int dimensionI = 0; dimensionI < _numberOfDimensions; dimensionI++)
                            groupAverageVector[dimensionI] += simplexVertex[dimensionI];
                    }

                    yield return groupAverageVector;
                }
            }
        }


    }

    public class P2pConnectionValueCalculator
    {
        int NumberOfDimensions => _localPeerVector.Length;
        float[] _localPeerVector;
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

        readonly int[] _currentNeighborsCountPerSectors;
        readonly float[] _emptyDirectionVector; // null if not found
        readonly bool _thereIsNeighborAlongEmptyDirectionVector;

        readonly VectorSectorIndexCalculator _vsic;
        static readonly Dictionary<int, VectorSectorIndexCalculator> _vsics = new Dictionary<int, VectorSectorIndexCalculator>();

      
        internal static double[] FindEmptyDirection(int numberOfDimensions, VectorSectorIndexCalculator vsic, List<float[]> unitVectorsFromLocalPeerToNeighbors) // solves a linear inequation
        {
            foreach (var directionVector in vsic.EnumerateDirections())
            {
                // are all vectors along directionVector?
                bool neighbor_along_directionVector_exists = false;
                foreach (var unitVectorFromLocalPeerToNeighbor in unitVectorsFromLocalPeerToNeighbors)
                {
                    double multProduct = 0;
                    for (int dimensionI = 0; dimensionI < numberOfDimensions; dimensionI++)
                        multProduct += unitVectorFromLocalPeerToNeighbor[dimensionI] * directionVector[dimensionI];
                    if (multProduct > 0)
                    {
                        neighbor_along_directionVector_exists = true;
                        break;
                    }
                }
                if (neighbor_along_directionVector_exists == false)
                    return directionVector;
            }

            return null;
        }

        public P2pConnectionValueCalculator(float[] localPeerVector, IEnumerable<float[]> currentNeighborsVectors, Action<string> wtl)
        { 
            _localPeerVector = localPeerVector;
            if (!_vsics.TryGetValue(NumberOfDimensions, out _vsic))
            {
                _vsic = new VectorSectorIndexCalculator(NumberOfDimensions);
                _vsics.Add(NumberOfDimensions, _vsic);
            }
            _currentNeighborsCountPerSectors = new int[_vsic.IndexesCount];

            int neighborIndex = 0;
            var vectorsFromLocalPeerToNeighbors = new List<float[]>();
            foreach (var neighborVector in currentNeighborsVectors)
            {
                var vectorFromLocalPeerToNeighbor = new float[NumberOfDimensions];
                float vectorFromLocalPeerToNeighbor_length = 0;
                for (int i = 0; i < NumberOfDimensions; i++)
                {
                    var vectorFromLocalPeerToNeighbor_i = RegistrationIdDistance.GetDifferenceInLoopedRegistrationIdSpace(_localPeerVector[i], neighborVector[i]);
                    vectorFromLocalPeerToNeighbor[i] = vectorFromLocalPeerToNeighbor_i;
                    vectorFromLocalPeerToNeighbor_length += vectorFromLocalPeerToNeighbor_i * vectorFromLocalPeerToNeighbor_i;
                }
                vectorFromLocalPeerToNeighbor_length = (float)Math.Sqrt(vectorFromLocalPeerToNeighbor_length);
                var sectorIndex = _vsic.GetSectorIndex(vectorFromLocalPeerToNeighbor);
                _currentNeighborsCountPerSectors[sectorIndex]++;


                for (int i = 0; i < NumberOfDimensions; i++)
                {
                    vectorFromLocalPeerToNeighbor[i] /= vectorFromLocalPeerToNeighbor_length;
                }
                wtl?.Invoke($"neighbor#{neighborIndex} localToNeighbor=[{String.Join(";", vectorFromLocalPeerToNeighbor.Select(x => x.ToString()))}] sectorIndex={sectorIndex}");

                vectorsFromLocalPeerToNeighbors.Add(vectorFromLocalPeerToNeighbor);

                neighborIndex++;
            }

            _emptyDirectionVector = null;

            //_emptyDirectionVector = FindEmptyDirection(NumberOfDimensions, _vsic, vectorsFromLocalPeerToNeighbors);
            //if (_emptyDirectionVector != null)
            //{
            //    _thereIsNeighborAlongEmptyDirectionVector = false;
            //    neighborIndex = 0;
            //    foreach (var vectorFromLocalPeerToNeighbor in vectorsFromLocalPeerToNeighbors)
            //    {
            //        float mulResult = 0;
            //        for (int i = 0; i < NumberOfDimensions; i++)
            //            mulResult += vectorFromLocalPeerToNeighbor[i] * _emptyDirectionVector[i];
            //        wtl?.Invoke($"neighbor#{neighborIndex}=[{String.Join(";", vectorFromLocalPeerToNeighbor.Select(x => x.ToString()))}]:mulResult={mulResult};");
            //        if (mulResult > 0)
            //        {
            //            _thereIsNeighborAlongEmptyDirectionVector = true;
            //            break;
            //        }
            //        neighborIndex++;
            //    }
            //}
            
            if (wtl != null)
            {
                for (int sectorIndex = 0; sectorIndex < _vsic.IndexesCount; sectorIndex++)
                    wtl.Invoke($"sector{sectorIndex} simplexVector=[{String.Join(";", _vsic.GetSimplexVector(sectorIndex).Select(x => x.ToString()))}]");
                if (_emptyDirectionVector != null) wtl.Invoke($"emptyDirectionVector=[{String.Join(";", _emptyDirectionVector.Select(x => x.ToString()))}]");             
                else wtl.Invoke($"emptyDirectionVector=null");
            }
        }

        const float EmptySectorOccupationValue = 10;
        internal static float ValueToKeepConnectionAlive_SoftLimitNeighborsCountCases = 5.0f;

        public static float MutualValueToKeepConnectionAlive_SoftLimitNeighborsCountCases => ValueToKeepConnectionAlive_SoftLimitNeighborsCountCases * 2;


        public static double GetMutualP2pConnectionValue(ICryptoLibrary cryptoLibrary, RegistrationId registrationId1, ushort neighborsBusySectorIds1,
            RegistrationId registrationId2, ushort neighborsBusySectorIds2, int numberOfDimensions,
            bool thisConnectionAlreadyExists, bool anotherNeighborToSameSectorExists1, bool anotherNeighborToSameSectorExists2)
        {
            double r = 0;
            var distance = registrationId1.GetDistanceTo(cryptoLibrary, registrationId2, numberOfDimensions).ToDouble();
            r -= distance;

            if (thisConnectionAlreadyExists)
            {
                if (anotherNeighborToSameSectorExists1 == false) r += EmptySectorOccupationValue;
                if (anotherNeighborToSameSectorExists2 == false) r += EmptySectorOccupationValue;
            }
            else
            {
                var vsic = new VectorSectorIndexCalculator(numberOfDimensions);
                var vector1to2 = RegistrationId.GetDifferenceVectorF(registrationId1, registrationId2, cryptoLibrary, numberOfDimensions);
                var vector1to2SectorIndex = vsic.GetSectorIndex(vector1to2);
                var vector1to2IsInVacantSector = ((neighborsBusySectorIds1 >> vector1to2SectorIndex) & 0x0001) == 0;
                if (vector1to2IsInVacantSector) r += EmptySectorOccupationValue;

                var vector2to1 = new float[numberOfDimensions];
                for (int i = 0; i < numberOfDimensions; i++) vector2to1[i] = -vector1to2[i];
                var vector2to1SectorIndex = vsic.GetSectorIndex(vector2to1);

                var vector2to1IsInVacantSector = ((neighborsBusySectorIds2 >> vector2to1SectorIndex) & 0x0001) == 0;
                if (vector2to1IsInVacantSector) r += EmptySectorOccupationValue;
            }

            return r;
        }


        ///<param name="considerValueOfUniqueSectors">false when registering via EP</param>
        /// <returns>component of mutual value</returns>
        public float GetValue(float[] neighborVector, bool neighborIsAlreadyConnected, bool considerValueOfUniqueSectors)
        {                      
            float distanceFromLocalPeerToNeighbor = 0;
            var vectorFromLocalPeerToNeighbor = new float[NumberOfDimensions];
            for (int i = 0; i < neighborVector.Length; i++)
            {
                var vectorFromLocalPeerToNeighbor_i = RegistrationIdDistance.GetDifferenceInLoopedRegistrationIdSpace(_localPeerVector[i], neighborVector[i]);
                vectorFromLocalPeerToNeighbor[i] = vectorFromLocalPeerToNeighbor_i;
                distanceFromLocalPeerToNeighbor += vectorFromLocalPeerToNeighbor_i * vectorFromLocalPeerToNeighbor_i;
            }
            distanceFromLocalPeerToNeighbor = (float)Math.Sqrt(distanceFromLocalPeerToNeighbor);
            float r = -distanceFromLocalPeerToNeighbor;

            if (considerValueOfUniqueSectors)
            {
                var sectorIndex = _vsic.GetSectorIndex(vectorFromLocalPeerToNeighbor);
                var neighborsCountInSector = _currentNeighborsCountPerSectors[sectorIndex];
                if (neighborIsAlreadyConnected)
                {
                    if (neighborsCountInSector == 1) r += EmptySectorOccupationValue; // this neighbor is the only one in the sector
                 //   else if (neighborsCountInSector == 2) r += 1.0f; // TODO questionable heuristics really 1 or another value?
                }
                else
                {
                    if (neighborsCountInSector == 0) r += EmptySectorOccupationValue;
                  //  else if (neighborsCountInSector == 1) r += 1.0f; // TODO questionable heuristics really 1 or another value?
                }
            }
            return r;
        }

        public string GetP2pConnectionsPainSignal(bool returnDetailsIfAllGood)
        {
            var r = new StringBuilder();
            for (int sectorIndex = 0; sectorIndex < _currentNeighborsCountPerSectors.Length; sectorIndex++)
            {
                if (_currentNeighborsCountPerSectors[sectorIndex] == 0)
                    r.Append($"sector{sectorIndex}:zero;");
                else if (returnDetailsIfAllGood)
                    r.Append($"sector{sectorIndex}:{_currentNeighborsCountPerSectors[sectorIndex]};");
            }

         
            if (_emptyDirectionVector != null && !_thereIsNeighborAlongEmptyDirectionVector)
            {
                r.Append($"noNeighbAlongEmptyDirection;");
            }


            if (r.Length != 0) return r.ToString();
            else return null;
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
        public override string ToString() => MiscProcedures.ByteArrayToString(Ecdh25519PublicKey);
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
