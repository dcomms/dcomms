using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.SUBT
{
    public class SubtLocalPeerConfiguration
    {
        /// <summary>
        /// bits per second
        /// makes sense only for role=user
        /// </summary>
        public float BandwidthTarget { get; set; }
        public int SenderThreadsCount { get; set; } = 4;
        public float Speed100ms { get; set; } = 0.005f;
        public float Speed100msLimit { get; set; } = 0.03f;
    }
}
