﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Dcomms.NAT
{
	class SocketGroup
	{
		Dictionary<UdpClient, List<IPAddress>> Sockets { get; }
		SemaphoreSlim SocketSendLocker { get; }
		int DefaultPort { get ; }

		public SocketGroup (Dictionary<UdpClient, List<IPAddress>> sockets, int defaultPort)
		{
			Sockets = sockets;
			DefaultPort = defaultPort;
			SocketSendLocker = new SemaphoreSlim (1, 1);
		}

		public async Task<(IPAddress, UdpReceiveResult)> ReceiveAsync(CancellationToken token)
		{
			while (!token.IsCancellationRequested)
            {
				foreach (var keypair in Sockets)
                {
					try 
                    {
						if (keypair.Key.Available > 0) {
							var localAddress = ((IPEndPoint)keypair.Key.Client.LocalEndPoint).Address;
							var data = await keypair.Key.ReceiveAsync();
							return (localAddress, data);
						}
					} catch (Exception) {
						// Ignore any errors ///???????????
					}
				}
				await Task.Delay(10, token);
            }
            return (null, default(UdpReceiveResult));
        }

		public async Task SendAsync (byte [] buffer, IPAddress gatewayAddressNullable, CancellationToken token)
		{
			using (await SocketSendLocker.DisposableWaitAsync (token)) 
            {
				foreach (var socket in Sockets) 
                {
					try 
                    {
						if (gatewayAddressNullable == null) 
                        {
							foreach (var defaultGateway in socket.Value)
								await socket.Key.SendAsync(buffer, buffer.Length, new IPEndPoint(defaultGateway, DefaultPort));
						} 
                        else
							await socket.Key.SendAsync(buffer, buffer.Length, new IPEndPoint(gatewayAddressNullable, DefaultPort));						
					} 
                    catch (Exception) 
                    { ////??????????????
					}
				}
			}
		}
	}
}
