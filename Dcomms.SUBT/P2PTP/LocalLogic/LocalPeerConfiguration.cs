using Dcomms.P2PTP.Extensibility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Dcomms.P2PTP.LocalLogic
{   
    /// <summary>
    /// settings for the local peer (P2PTS core), initialized by user's application
    /// </summary>
    public class LocalPeerConfiguration
    {
        /// <summary>
        /// if null - opens random local UDP port
        /// </summary>
        public ushort? LocalUdpPortRangeStart { get; set; }

        public ushort? DesiredLocalUdpPortRangeStart { get; set; }
        /// <summary>
        /// number of UDP sockets and receiver threads to create. is used to scale receivers across CPU cores
        /// </summary>
        public int SocketsCount { get; set; } = 1;
        /// <summary>
        /// initially known 'entry points' to the P2PTS network
        /// </summary>
        public IPEndPoint[] Coordinators;
        public Vision.VisionChannel VisionChannel;
        public string VisionChannelSourceId;
        public string CoordinatorsString
        {
            get
            {
                if (Coordinators == null) return "";
                return String.Join(";", Coordinators.Select(x => x.ToString()));
            }
            set
            {
                if (String.IsNullOrEmpty(value)) Coordinators = null;
                else Coordinators = (from valueStr in value.Split(';')
                                     let pos = valueStr.IndexOf(':')
                                     where pos != -1
                                     select new IPEndPoint(
                                         IPAddress.Parse(valueStr.Substring(0, pos)),
                                         int.Parse(valueStr.Substring(pos + 1))
                                         )
                        ).ToArray();
            }
        }


        /// <summary>
        /// user's app, having test instructions - continuous speed test
        /// </summary>
        public bool RoleAsUser { get; set; }
        /// <summary>
        /// shared peer for the tests, without test instructions (startrinity servers to assure permanent testing capacity) 
        /// softswitches?
        /// </summary>
        public bool RoleAsSharedPassive { get; set; }
        /// <summary>
        /// coordinator is accessible on public IP, is used as entry point to new peers
        /// </summary>
        public bool RoleAsCoordinator { get; set; }



        /// <summary>
        /// not null, can be empty
        /// list of extensions/plugins/applications running by this peer
        /// </summary>
        public ILocalPeerExtension[] Extensions;
    }
}
