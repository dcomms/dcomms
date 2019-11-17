using Dcomms.P2PTP.LocalLogic;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.SUBT.SUBTP
{
    /// <summary>
    /// specifies type of SUBT signaling packet
    /// 1 byte
    /// </summary>
    enum SubtPacketType
    {
        RemoteStatus = 1,
        RttRequestResponse = 2,
        AdjustmentRequest = 5
        // others are reserved
    }
}
