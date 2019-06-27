using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.CCP
{
    /// <summary>
    /// CCP over UDP
    /// </summary>
    public class CcpUrl
    {
        public string Host { get; set; }
        public ushort Port { get; set; }
        public CcpUrl(string urlStr)
        { // ccp://localhost:9523
            const string prefix = "ccp://";
            if (!urlStr.StartsWith(prefix)) throw new ArgumentException();
            var hostAndPort = urlStr.Substring(prefix.Length);
            var colonIndex = hostAndPort.IndexOf(':');
            Host = hostAndPort.Substring(0, colonIndex);
            Port = ushort.Parse(hostAndPort.Substring(colonIndex + 1));
        }
    }
    /// <summary>
    /// CCP over DRP (to hide the center)
    /// </summary>
    class CcpdUrl
    {
        public string Id { get; set; }
    }
}
