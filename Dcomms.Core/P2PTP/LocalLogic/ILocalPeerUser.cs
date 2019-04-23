using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.P2PTP.LocalLogic
{
    /// <summary>
    /// interface to user's application (GUI) for the P2PTS core
    /// </summary>
    public interface ILocalPeerUser
    {
        void WriteToLog(string message);
        bool EnableLog { get; }
    }
}
