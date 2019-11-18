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


        public float MaxLocalTxBandwidth = 1024 * 1024 * 100;
        public float MaxLocalTxBandwidthMbps
        {
            get => MaxLocalTxBandwidth / 1024 / 1024;
            set { MaxLocalTxBandwidth = value * 1024 * 1024; }
        }
    }
}
