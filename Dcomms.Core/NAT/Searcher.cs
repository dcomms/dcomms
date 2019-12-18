//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2019 Alan McGovern
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
using System.Threading.Tasks;

namespace Dcomms.NAT
{
	abstract class Searcher: IDisposable 
	{
        readonly Action<NatRouterDevice> _deviceFound;

        public bool Listening => ListeningTask != null;
		public abstract NatConfigurationProtocol Protocol { get; }

		Task ListeningTask { get; set; }
		protected SocketGroup Clients { get; }

		CancellationTokenSource ListeningTask_CancellationTokenSource;
		protected CancellationTokenSource CurrentSearchCancellationTokenSource;
		CancellationTokenSource OverallSearchCancellationTokenSource;
		Task SearchTask { get; set; }
        protected NatUtility NU { get; private set; }
		protected Searcher(SocketGroup clients, NatUtility nu, Action<NatRouterDevice> deviceFound)
		{
            NU = nu;
			Clients = clients;
            _deviceFound = deviceFound;
		}

		protected void BeginListening()
		{
			// Begin listening, if we are not already listening.
			if (!Listening) {
				ListeningTask_CancellationTokenSource?.Cancel();
				ListeningTask_CancellationTokenSource = new CancellationTokenSource();
				ListeningTask = ListenAsync(ListeningTask_CancellationTokenSource.Token);
			}
		}
		async Task ListenAsync(CancellationToken token)
		{
			while (!token.IsCancellationRequested)
            {
                (var localAddress, var data) = await Clients.ReceiveAsync(token).ConfigureAwait(false);
                if (localAddress != null) 
                    await HandleInitialResponse(localAddress, data, token).ConfigureAwait(false);               
			}
		}

		protected abstract Task HandleInitialResponse(IPAddress localAddress, UdpReceiveResult result, CancellationToken token);
		public async Task SearchAsync()
		{
			// Cancel any existing continuous search operation.
			OverallSearchCancellationTokenSource?.Cancel();
			if (SearchTask != null)
				await SearchTask.CatchExceptions(NU);

			// Create a CancellationTokenSource for the search we're about to perform.
			BeginListening();
			OverallSearchCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ListeningTask_CancellationTokenSource.Token);

			SearchTask = SearchAsync(null, OverallSearchCancellationTokenSource.Token);
			await SearchTask;
		}
		//public async Task SearchAsync(IPAddress gatewayAddress)
		//{
		//	BeginListening();
		//	await SearchAsync(gatewayAddress, ListeningTask_CancellationTokenSource.Token).ConfigureAwait(false);
		//}

		protected abstract Task SearchAsync(IPAddress gatewayAddressNullable, CancellationToken token);

		public void Dispose()
		{
          //  ListeningTask?.WaitAndForget(NU);
		//	SearchTask?.WaitAndForget(NU);

			ListeningTask_CancellationTokenSource?.Cancel();
			ListeningTask_CancellationTokenSource = null;
			ListeningTask = null;

            OverallSearchCancellationTokenSource?.Cancel();
            OverallSearchCancellationTokenSource = null;
			SearchTask = null;

        }

		protected void RaiseDeviceFound(NatRouterDevice device)
		{
			CurrentSearchCancellationTokenSource?.Cancel();
            _deviceFound(device);
		}	
	}
}
