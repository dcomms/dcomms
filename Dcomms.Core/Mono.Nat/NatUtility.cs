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
        public async Task<bool> SearchAndConfigure(int[] localUdpPorts, int timeoutS = 20)
        {
            _upnpSearcher = new UpnpSearcher(this, d => Configure(d, localUdpPorts));
            _pmpSearcher = new PmpSearcher(this, d => Configure(d, localUdpPorts));             
            _pmpSearcher.SearchAsync().FireAndForget(this);
            _upnpSearcher.SearchAsync().FireAndForget(this);

            var sw = Stopwatch.StartNew();
            for (; ; )
            {
                await Task.Delay(10);
                if (_succeeded) return true;
                if (sw.Elapsed.TotalSeconds > timeoutS) return false;
            }
        }
        async void Configure(NatRouterDevice device, int[] localUdpPorts)
        { 
            try
            {
                Log_deepDetail($">> NatUtility.Configure(localUdpPorts={String.Join(";", localUdpPorts.Select(x=>x.ToString()))}) device={device}");

                var externalIP = await device.GetExternalIPAsync();
                Log_deepDetail($"device={device}, externalIP={externalIP}");
                if (externalIP == null) return;
                if (externalIP.ToString() == "0.0.0.0") return;
                if (externalIP.ToString() == "127.0.0.1") return;

                var localUdpPortsHS = new HashSet<int>(localUdpPorts);
                var existingMappings = await device.GetAllMappingsAsync();
                foreach (var existingMapping in existingMappings)
                {
                    if (existingMapping.Protocol == IpProtocol.Udp)
                    {
                        if (localUdpPortsHS.Contains(existingMapping.PrivatePort))
                        {
                            Log_deepDetail($"mapping already exists at router: {existingMapping.Description} {existingMapping.PrivatePort}-{existingMapping.PublicPort} {existingMapping.Protocol}");
                            localUdpPortsHS.Remove(existingMapping.PrivatePort);
                        }
                        else
                        {
                            try
                            {
                                Log_deepDetail($"deleting unused existing mapping: {existingMapping.Description} {existingMapping.PrivatePort}-{existingMapping.PublicPort} {existingMapping.Protocol}");
                                await device.DeletePortMappingAsync(existingMapping);
                            }
                            catch (Exception exc)
                            {
                                Log_deepDetail($"could not delete existing unused mapping to UDP port {existingMapping.PrivatePort} at {device}: {exc.Message}");
                            }
                        }
                    }
                }

                if (localUdpPortsHS.Count == 0)
                {
                    Log_higherLevelDetail($"no new mappings to create");
                    _succeeded = true;
                    return;
                }

                foreach (var localUdpPort in localUdpPortsHS)
                {
                    try
                    {
                        var mapping2 = new Mapping(IpProtocol.Udp, localUdpPort, localUdpPort); // todo use   public port = *  (0)
                        try
                        {
                            await device.CreatePortMappingAsync(mapping2);
                            Log_higherLevelDetail($"successfully created mapping: externalIP={externalIP}, protocol={mapping2.Protocol}, publicPort={mapping2.PublicPort}, privatePort={mapping2.PrivatePort}");
                            _succeeded = true;
                        }
                        catch (MappingException exc)
                        {
                            Log_deepDetail($"first-trial error when adding port mapping to {device}: {exc.Message}. trying again...");
                            try
                            {
                                await device.DeletePortMappingAsync(mapping2);
                                await device.CreatePortMappingAsync(mapping2);
                                Log_higherLevelDetail($"successfully created mapping (2nd trial): externalIP={externalIP}, protocol={mapping2.Protocol}, publicPort={mapping2.PublicPort}, privatePort={mapping2.PrivatePort}");
                                _succeeded = true;
                            }
                            catch (MappingException exc2)
                            {
                                Log_mediumPain($"second-trial error when adding port {localUdpPort} mapping to {device}: {exc2.Message}");
                            }
                        }
                    }
                    catch (Exception exc)
                    {                  
                        Log_mediumPain($"error when creating mapping for port {localUdpPort} at NAT device {device}: {exc}");               
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

            }
            catch (Exception exc)
            {
                Log_mediumPain($"error when configuring NAT device {device}: {exc}");
            }

        }

        internal void Log_deepDetail(string message)
        {
            _visionChannel?.Emit(_visionChannelSourceId, VisionChannelModuleName, AttentionLevel.deepDetail, message);
        }
        internal void Log_higherLevelDetail(string message)
        {
            _visionChannel?.Emit(_visionChannelSourceId, VisionChannelModuleName, AttentionLevel.higherLevelDetail, message);
        }
        internal void Log_mediumPain(string message)
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
