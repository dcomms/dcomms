using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms
{
    /// <summary>
    /// thread-unsafe
    /// element of PoW validation (anti-DDoS) subsystem
    /// 
    /// still possible DoS attack#1: attacker precalculates hashes for some [future] period and sends a burst of hello0 packets, and for this time there will be 
    /// LESS unique (free) hashes for legitimate users
    /// 
    /// still possible attack #2: attacker re-sends previously sent valid PoW
    /// 
    /// stores hashes, results of SHA, previous N _valid_ PoW's
    /// provides a fast routine that checks "if the new valid PoW unique?", and this routine can return false positives (intentionally designed, to get fastest performance)
    /// contains the unique quadruples (dwords)   for previous "time unit" ("10min") (when new "time unit" comes, this container becomes reset)
    /// 
    /// 
    /// server can check 400K hashes per sec
    /// client can send unique PoW every 200ms (average)
    /// keep previous valid requests (hashes) for periodK
    /// </summary>
    public class UniqueDataTracker
    {
        /// <summary>
        /// consider loop for every group of 4 bytes in input data: bytes A,B,C,D  (quadruple, double word, DWORD)
        /// if the ABCD value exists in this container, a bit is set to 1 at element index [A*65536+B*256+C], bit index [D mod 8]
        /// where 5 bits of D are ignored (they can be non-unique in this container)
        ///  
        /// having capacity of 256**4 = 4.3E9 unique values, it can accept 7.15M unique values per second
        /// takes 16MB of RAM, not too big for modern devices, but 
        /// 
        /// against (precalculated) attack #1: if reset period is 10 minutes,
        /// then for a 10-minute-duration attack it needs 4.3E9 valid and unique PoW values. if it takes 300ms to calculate PoW, 
        /// it requires 1.3E9 seconds of single-core CPU time = 466 days using 32-core CPU
        /// 
        /// counter-measure against attack #1: if server sees that it is under attack (too many unique values filled) - then it automatically resets the unique values
        /// 
        /// </summary>
        byte[] _dwordFlagBits = new byte[256 * 256 * 256];
        uint _uniqueValuesCount, _uniqueValuesOverflowCount;
        bool UniqueValuesCapacityOverflowFlag => _uniqueValuesCount > _uniqueValuesOverflowCount;

        uint _latestResetTimeSec32UTC;
        readonly uint _recentUniqueDataResetPeriodS;
        public UniqueDataTracker(uint timeSec32UTC, uint recentUniqueDataResetPeriodS)
        {
            _recentUniqueDataResetPeriodS = recentUniqueDataResetPeriodS;
            _uniqueValuesOverflowCount = (uint)((double)_dwordFlagBits.Length * 256 * 0.3);
            Reset(timeSec32UTC, false);
        }
        void Reset(uint timeSec32UTC, bool resetDwordFlagBits = true)
        {
            _latestResetTimeSec32UTC = timeSec32UTC;
            if (resetDwordFlagBits) _dwordFlagBits.Initialize();
            _uniqueValuesCount = 0;
        }
        public unsafe bool TryInputData(byte[] inputData, uint timeSec32UTC)
        {
            if (inputData == null) throw new ArgumentNullException(nameof(inputData));
            if (inputData.Length % 4 != 0) throw new ArgumentException(nameof(inputData)); // must be of size N*4

            if (unchecked(timeSec32UTC - _latestResetTimeSec32UTC) > _recentUniqueDataResetPeriodS)
                Reset(timeSec32UTC);

            int numberOfDwords = inputData.Length << 2;

            fixed (byte* dwordFlagBitsPtr = _dwordFlagBits)
            {
                fixed (byte* inputDataPtr = inputData)
                {
                    uint* inputDataPtr32 = (uint*)inputDataPtr;
                    for (int i = 0; i < numberOfDwords; i++, inputDataPtr32++)
                    {
                        uint dword = *inputDataPtr32;
                        uint dwordFlagsIndex = dword & 0x00FFFFFF;
                        var dwordFlagBitIndex = (byte)(((dword & 0xFF000000) >> 24) & 0b00000111);
                        var dwordFlagBitMask = (byte)(1 << dwordFlagBitIndex);
                        byte* dwordFlagBits = dwordFlagBitsPtr + dwordFlagsIndex;
                        if (((*dwordFlagBits) & dwordFlagBitMask) != 0)
                        { // DWORD is not unique
                            { // unset flags for previously enumerated dword's (mark as "unused") , i.e. clean bits set in current procedure call                               
                                for (int j = i; ;)
                                {
                                    j--;
                                    if (j < 0) break;
                                    inputDataPtr32--;

                                    dword = *inputDataPtr32;
                                    dwordFlagsIndex = dword & 0x00FFFFFF;
                                    dwordFlagBitIndex = (byte)(((dword & 0xFF000000) >> 24) & 0b00000111);
                                    dwordFlagBitMask = (byte)(1 << dwordFlagBitIndex);

                                    dwordFlagBits = dwordFlagBitsPtr + dwordFlagsIndex;

                                    // set bit back to 0
                                    *dwordFlagBits &= (byte)(~dwordFlagBitMask);
                                }
                            }
                            return false;
                        }
                        *dwordFlagBits |= dwordFlagBitMask; // mark this dword as "used"
                    }
                }
            }

            _uniqueValuesCount++;
            if (UniqueValuesCapacityOverflowFlag)
            {
                // counter-measure against attack #1: if server sees that it is under attack (too many unique values filled) - then it automatically resets the unique values
                Reset(timeSec32UTC);
            }
            return true;
        }
    }
}
