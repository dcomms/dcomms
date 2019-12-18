using System;
using System.Net;

namespace Dcomms.NAT.Upnp
{
	interface IRequestMessage
	{
		WebRequest Encode (out byte [] body);
	}
}
