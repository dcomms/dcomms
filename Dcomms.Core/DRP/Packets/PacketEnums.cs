using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DRP.Packets
{
    public enum ResponseOrFailureCode
    {
        /// <summary>
        /// is sent to previous hop immediately when packet is proxied, to stop retransmission timer
        /// request packet is retransmitted until the NPACK with status=trying is received
        /// </summary>
        accepted = 0,


        /// <summary>
        /// 
        /// - route not found (no neighbor found to forward the request)
        /// - overloaded
        /// - loop detected at proxy (this proxy is already proxying the request)
        /// 
        /// proxy peer MAY send the rejected request to another peer (reroute the request)
        /// </summary>
        failure_routeIsUnavailable = 1,
        failure_numberOfHopsRemainingReachedZero = 2

    }
}
