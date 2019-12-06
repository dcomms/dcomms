using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms
{
    public enum PacketTypes
    {
        #region P2PTP
        hello = 1,

        /// <summary>
        /// is shared regularly between peers
        /// is accepted by client peers
        /// is ignored by server peers. servers themselves know many connected clients and send the peersList to the clients, so clients know about each other
        /// </summary>
        peersListIpv4 = 5,
        /// <summary>
        /// reserved for future use
        /// </summary>
        peersListIpv4and6 = 6,

        /// <summary>
        /// contain ensapsulated extension-specific data
        /// are processed by manager thread
        /// </summary>
        extensionSignaling = 7,
        #endregion

        #region DRP
        RegisterPow1Request = 21,
        RegisterPow1Response = 22,
        RegisterReq = 23,
        NeighborPeerAck = 24,
        RegisterAck1 = 25,
        RegisterAck2 = 26,
        Ping = 27,
        Pong = 28,
        RegisterConfirmation = 29,
        InviteReq = 30,
        InviteAck1 = 31,
        InviteAck2 = 32,
        InviteCfm = 33,
        Failure = 34,
        #endregion

        #region DMP (direct channel)
        DmpPing = 50,
        DmpPong = 51,
        MessageStart = 52,
        MessagePart = 53,
        MessageAck = 54,
        #endregion

        NatTest1Request = 70,
        NatTest1Response = 71,
    }
}
