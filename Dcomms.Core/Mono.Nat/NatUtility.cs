//
// Authors:
//   Ben Motmans <ben.motmans@gmail.com>
//   Nicholas Terry <nick.i.terry@gmail.com>
//
// Copyright (C) 2007 Ben Motmans
// Copyright (C) 2014 Nicholas Terry
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Linq;
using System.Collections.Generic;
using System.IO;

using Mono.Nat.Pmp;
using Mono.Nat.Upnp;
using System.Threading.Tasks;
using Dcomms.Vision;
using System.Diagnostics;

namespace Mono.Nat
{
	public class NatUtility: IDisposable
	{
        //	public event EventHandler<DeviceEventArgs> DeviceFound;
        //	public event EventHandler<DeviceEventArgs> DeviceLost;

        //readonly object Locker = new object();

        readonly VisionChannel _visionChannel;
        readonly string _visionChannelSourceId;
        const string VisionChannelModuleName = "NAT";

        public bool IsSearching => _upnpSearcher.Listening || _pmpSearcher.Listening;

        UpnpSearcher _upnpSearcher;
        PmpSearcher _pmpSearcher;
        bool _succeeded;
        public NatUtility(VisionChannel visionChannel, string visionChannelSourceId)
        {
            _visionChannel = visionChannel;
            _visionChannelSourceId = visionChannelSourceId;            
        }
        public async Task<bool> SearchAndConfigure(TimeSpan timeout, int localUdpPort)
        {
            _upnpSearcher = new UpnpSearcher(this, d => Configure(d, localUdpPort));
            _pmpSearcher = new PmpSearcher(this, d => Configure(d, localUdpPort));             
            _pmpSearcher.SearchAsync().FireAndForget(this);
            _upnpSearcher.SearchAsync().FireAndForget(this);

            var sw = Stopwatch.StartNew();
            for (; ; )
            {
                await Task.Delay(10);
                if (_succeeded) return true;
                if (sw.Elapsed > timeout) return false;
            }
        }
        async void Configure(INatDevice device, int localUdpPort)
        { 
            try
            {
                Log($">>NatUtility.Configure(localUdpPort={localUdpPort}) device={device}");

                var externalIP = await device.GetExternalIPAsync();
                Log($"device={device}, externalIP={externalIP}");
                if (externalIP == null) return;
                if (externalIP.ToString() == "0.0.0.0") return;
                if (externalIP.ToString() == "127.0.0.1") return;
                
                //Console.WriteLine("IP: {0}", await device.GetExternalIPAsync());
                // try to create a new port map:           
                var mapping2 = new Mapping(Mono.Nat.Protocol.Udp, localUdpPort, localUdpPort);
                try
                {
                    await device.CreatePortMapAsync(mapping2);
                    Log($"successfully created mapping: externalIP={externalIP}, protocol={mapping2.Protocol}, publicPort={mapping2.PublicPort}, privatePort={mapping2.PrivatePort}");
                }
                catch (MappingException exc)
                {
                    Log($"first-trial error when adding port mapping to {device}: {exc.Message}. trying to delete and add again...");
                    try
                    {
                        await device.DeletePortMapAsync(mapping2);
                        await device.CreatePortMapAsync(mapping2);
                        Log($"successfully created mapping (2nd trial): externalIP={externalIP}, protocol={mapping2.Protocol}, publicPort={mapping2.PublicPort}, privatePort={mapping2.PrivatePort}");
                    }
                    catch (MappingException exc2)
                    {
                        LogError($"second-trial error when adding port mapping to {device}: {exc2.Message}");
                    }
                }


                // Try to retrieve confirmation on the port map we just created:               
                //try
                //{
                //    var m = await device.GetSpecificMappingAsync(Mono.Nat.Protocol.Udp, 6020);
                //    WriteToLog_drpGeneral_guiActivity($"Verified Mapping: externalIP={externalIP}, protocol={m.Protocol}, publicPort={m.PublicPort}, privatePort={m.PrivatePort}");
                //}
                //catch (Exception exc)
                //{
                //    WriteToLog_drpGeneral_guiActivity($"Couldn't verify mapping (GetSpecificMappingAsync failed): {exc}");
                //}

                _succeeded = true;
            }
            catch (Exception exc)
            {
                LogError($"error when configuring NAT device {device}: {exc}");
            }

        }

        internal void Log(string message)
        {
            _visionChannel?.Emit(_visionChannelSourceId, VisionChannelModuleName, AttentionLevel.guiActivity, message);
        }
        internal void LogError(string message)
        {
            _visionChannel?.Emit(_visionChannelSourceId, VisionChannelModuleName, AttentionLevel.mediumPain, message);          
        }

        ///// <summary>
        ///// Sends a single (non-periodic) message to the specified IP address to see if it supports the
        ///// specified port mapping protocol, and begin listening indefinitely for responses.
        ///// </summary>
        ///// <param name="gatewayAddress">The IP address</param>
        ///// <param name="type"></param>
        //public void Search (IPAddress gatewayAddress, NatProtocol type)
        //{
        //	lock (Locker) {
        //		if (type == NatProtocol.Pmp) {
        //			PmpSearcher.Instance.SearchAsync (gatewayAddress).FireAndForget ();
        //		} else if (type == NatProtocol.Upnp) {
        //			UpnpSearcher.Instance.SearchAsync (gatewayAddress).FireAndForget ();
        //		} else {
        //			throw new InvalidOperationException ("Unsuported type given");
        //		}
        //	}
        //}

        /// <summary>
        /// Periodically send a multicast UDP message to scan for new devices, and begin listening indefinitely
        /// for responses.
        /// </summary>
      //  public void StartDiscovery(params NatProtocol [] devices)
	//	{
	//	}

		/// <summary>
		/// Stop listening for responses to the search messages, and cancel any pending searches.
		/// </summary>
		public void Dispose ()
        {
            if (_pmpSearcher != null)
            {
                _pmpSearcher.Dispose();
                _pmpSearcher = null;
            }
            if (_upnpSearcher != null)
            {
                _upnpSearcher.Dispose();
                _upnpSearcher = null;
            }
		}
	}
}
