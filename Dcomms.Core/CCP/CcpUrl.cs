using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.CCP
{
    /// <summary>
    /// CP over UDP
    /// </summary>
    class CcpUrl
    {
        public string Host { get; set; }
        public ushort Port { get; set; }
        public CcpUrl(string urlStr)
        {
            throw new NotImplementedException();
        }
    }
    /// <summary>
    /// ccp over DRP
    /// </summary>
    class CcpdUrl
    {
        public string Id { get; set; }
    }
}
