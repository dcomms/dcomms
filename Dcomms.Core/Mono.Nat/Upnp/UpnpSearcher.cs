//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//   Ben Motmans <ben.motmans@gmail.com>
//   Nicholas Terry <nick.i.terry@gmail.com>
//
// Copyright (C) 2006 Alan McGovern
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Mono.Nat.Upnp
{
	class UpnpSearcher : Searcher
	{
		static SocketGroup GetSockets()
		{
			var clients = new Dictionary<UdpClient, List<IPAddress>> ();
			var gateways = new List<IPAddress> { IPAddress.Parse ("239.255.255.250") };

			try {
				foreach (NetworkInterface n in NetworkInterface.GetAllNetworkInterfaces ()) {
					foreach (UnicastIPAddressInformation address in n.GetIPProperties ().UnicastAddresses) {
						if (address.Address.AddressFamily == AddressFamily.InterNetwork) {
							try {
								var client = new UdpClient (new IPEndPoint (address.Address, 0));
								clients.Add (client, gateways);
							} catch {
								continue; // Move on to the next address.
							}
						}
					}
				}
			} catch (Exception) {
				clients.Add (new UdpClient (0), gateways);
			}

			return new SocketGroup (clients, 1900);
		}

		public override NatConfigurationProtocol Protocol => NatConfigurationProtocol.Upnp;
        readonly HashSet<Uri> AlreadyProcessedDeviceServiceUris = new HashSet<Uri>();
	
        public UpnpSearcher(NatUtility nu, Action<NatRouterDevice> deviceFound)
			: base (GetSockets(), nu, deviceFound)
		{
		}

		protected override async Task SearchAsync(IPAddress gatewayAddressNullable, CancellationToken token)
		{
            NU.Log_deepDetail($">> UpnpSearcher.SearchAsync() gatewayAddressNullable={gatewayAddressNullable}");
			var buffer = gatewayAddressNullable == null ? DiscoverDeviceMessage.EncodeSSDP() : DiscoverDeviceMessage.EncodeUnicast(gatewayAddressNullable);
            await Clients.SendAsync(buffer, gatewayAddressNullable, token);		
		}
        static string GetHeaderValue(List<string> headerLines, string headerName)
        {
            var headerLine = headerLines.FirstOrDefault(t => t.StartsWith(headerName, StringComparison.OrdinalIgnoreCase));
            if (headerLine == null) return null;

            var i0 = headerLine.IndexOf(':');
            if (i0 == -1) return null;
            i0++;
            if (i0 >= headerLine.Length) return null;
            if (headerLine[i0] == ' ') i0++;
            if (i0 >= headerLine.Length) return null;

            return headerLine.Substring(i0);
        }
		protected override async Task HandleInitialResponse(IPAddress localAddress, UdpReceiveResult result, CancellationToken token)
		{	
		    var dataString = Encoding.UTF8.GetString(result.Buffer);	
			try
            {
			//	NU.Log($"handling UPnP response (received via local interface {localAddress}): {dataString}");

				/* For UPnP Port Mapping we need ot find either WANPPPConnection or WANIPConnection. 
				 Any other device type is not good to us for this purpose. See the IGP overview paper 
				 page 5 for an overview of device types and their hierarchy.
				 http://upnp.org/specs/gw/UPnP-gw-InternetGatewayDevice-v1-Device.pdf */
				// TODO: Currently we are assuming version 1 of the protocol. We should figure out which version it is and apply the correct URN
				// Some routers don't correctly implement the version ID on the URN, so we only search for the type prefix.

				if (dataString.IndexOf("urn:schemas-upnp-org:service:WANIPConnection:", StringComparison.OrdinalIgnoreCase) != -1) 
					NU.Log_deepDetail("UPnP Response: router advertised a 'urn:schemas-upnp-org:service:WANIPConnection:1' service");
				else if (dataString.IndexOf("urn:schemas-upnp-org:service:WANPPPConnection:", StringComparison.OrdinalIgnoreCase) != -1) 
					NU.Log_deepDetail("UPnP Response: router advertised a 'urn:schemas-upnp-org:service:WANPPPConnection:' service");
				else
					return;

                var headerLines = dataString.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList();
                var locationHeaderValue = GetHeaderValue(headerLines, "location");
				if (locationHeaderValue == null) return;

				var deviceServiceUri = new Uri(locationHeaderValue);
				lock (AlreadyProcessedDeviceServiceUris)
                {
                    if (AlreadyProcessedDeviceServiceUris.Contains(deviceServiceUri))
                    {
                        NU.Log_deepDetail($"skipping uPnP service URL {deviceServiceUri}, it has been already processed");
                        return;
                    }
                    AlreadyProcessedDeviceServiceUris.Add(deviceServiceUri);
				}
                
                var serverHeaderValue = GetHeaderValue(headerLines, "server");

                // Once we've parsed the information we need, we tell the device to retrieve it's service list
                // Once we successfully receive the service list, the callback provided will be invoked.
				var d = await TryGetServices(serverHeaderValue, localAddress, deviceServiceUri, token).ConfigureAwait(false);
				if (d != null)
					RaiseDeviceFound(d);
			} catch (Exception ex) {
                NU.Log_mediumPain($"Unhandled exception when trying to decode response from router: {ex}. data: {dataString}");
			}
		}
		async Task<UpnpNatRouterDevice> TryGetServices(string serverHeaderValue, IPAddress localAddress, Uri deviceServiceUri, CancellationToken token)
        {
            NU.Log_deepDetail($"getting uPnP services list from {deviceServiceUri} server {serverHeaderValue}");

            // create a HTTPWebRequest to download the list of services the device offers
            var request = new GetServicesMessage(deviceServiceUri).Encode(out byte[] body);
			if (body.Length > 0)
				NU.Log_mediumPain("Error: Services Message contained a body");
			using (token.Register(() => request.Abort()))
			    using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
				    return await TryParseServices(serverHeaderValue, localAddress, deviceServiceUri, response).ConfigureAwait(false);
		}
		async Task<UpnpNatRouterDevice> TryParseServices(string serverHeaderValue, IPAddress localAddress, Uri deviceServiceUri, HttpWebResponse response)
		{
			int loopsCount = 0;
			byte[] buffer = new byte[10240];
			var servicesXml = new StringBuilder();
			var xmldoc = new XmlDocument();
			var s = response.GetResponseStream();

			if (response.StatusCode != HttpStatusCode.OK)
            {
				NU.Log_mediumPain($"{response.ResponseUri}: couldn't get services list: {response.StatusCode}");
				return null; // FIXME: This the best thing to do??
			}

			while (true)
            {
				var bytesRead = await s.ReadAsync(buffer, 0, buffer.Length);
				servicesXml.Append(Encoding.UTF8.GetString (buffer, 0, bytesRead));
				try {
					xmldoc.LoadXml(servicesXml.ToString ());
					break;
				} catch (XmlException) {
					// If we can't receive the entire XML within 5 seconds, then drop the connection
					// Unfortunately not all routers supply a valid ContentLength (mine doesn't)
					// so this hack is needed to keep testing our received data until it gets successfully
					// parsed by the xmldoc. Without this, the code will never pick up my router.
					if (loopsCount++ > 500) {
                        NU.Log_mediumPain($"{response.ResponseUri}: couldn't parse services list: {servicesXml}\r\nserver: {serverHeaderValue}");
						return null;
					}
					await Task.Delay(10);
				}
			}

         //   NU.Log($"{response.ResponseUri}: parsed services: {xmldoc.OuterXml}");
			XmlNamespaceManager ns = new XmlNamespaceManager (xmldoc.NameTable);
			ns.AddNamespace ("ns", "urn:schemas-upnp-org:device-1-0");
			XmlNodeList nodes = xmldoc.SelectNodes ("//*/ns:serviceList", ns);

			foreach (XmlNode node in nodes) {
				//Go through each service there
				foreach (XmlNode service in node.ChildNodes) {
					//If the service is a WANIPConnection, then we have what we want
					string serviceType = service["serviceType"].InnerText;
                   // NU.Log($"{response.ResponseUri}: Found service: {serviceType}");
					StringComparison c = StringComparison.OrdinalIgnoreCase;
					// TODO: Add support for version 2 of UPnP.
					if (serviceType.Equals ("urn:schemas-upnp-org:service:WANPPPConnection:1", c) ||
						serviceType.Equals ("urn:schemas-upnp-org:service:WANIPConnection:1", c)) {
						var controlUrl = new Uri (service ["controlURL"].InnerText, UriKind.RelativeOrAbsolute);
						IPEndPoint deviceEndpoint = new IPEndPoint (IPAddress.Parse (response.ResponseUri.Host), response.ResponseUri.Port);
                        NU.Log_deepDetail($"{response.ResponseUri}: found upnp service at: {controlUrl.OriginalString}");
						try {
							if (controlUrl.IsAbsoluteUri) {
								deviceEndpoint = new IPEndPoint (IPAddress.Parse (controlUrl.Host), controlUrl.Port);
                                NU.Log_deepDetail($"{deviceEndpoint}: new control url: {controlUrl}");
							} else {
								controlUrl = new Uri (deviceServiceUri, controlUrl.OriginalString);
							}
						} catch {
							controlUrl = new Uri (deviceServiceUri, controlUrl.OriginalString);
                            NU.Log_deepDetail($"{deviceEndpoint}: assuming control Uri is relative: {controlUrl}");
						}
                        NU.Log_deepDetail($"{deviceEndpoint}: handshake is complete");
						return new UpnpNatRouterDevice(NU, serverHeaderValue, localAddress, deviceEndpoint, controlUrl, serviceType);
					}
				}
			}

			//If we get here, it means that we didn't get WANIPConnection service, which means no uPnP forwarding
			//So we don't invoke the callback, so this device is never added to our lists
			return null;
		}
	}
}
