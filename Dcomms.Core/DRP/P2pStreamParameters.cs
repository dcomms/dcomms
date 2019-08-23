using Dcomms.Cryptography;
using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{


    ///// <summary>
    ///// parameters to transmit DRP pings and proxied packets between registered neighbors:
    ///// from local peer to remote peer (txParamaters)
    ///// from remote peer to local peer (rxParamaters)
    ///// is negotiated via REGISTER channel
    ///// all fields are encrypted when transmitted over REGISTER channel, using single-block AES and shared ECDH key
    ///// </summary>
    //public class P2pStreamParameters
    //{
    //}
}
