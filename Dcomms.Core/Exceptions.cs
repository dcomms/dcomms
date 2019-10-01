using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms
{
    class InsufficientResourcesException : ApplicationException
    {

    }
    class PossibleMitmException : ApplicationException
    {

    }
    class BrokenCipherException : PossibleMitmException // todo handle attacks that raise such exceptions
    {

    }
    class UnmatchedFieldsException : PossibleMitmException 
    {

    }
    class BadSignatureException : PossibleMitmException 
    {

    }
    class BadUserCertificateException : BadSignatureException
    {

    }
    class CertificateOutOfDateException : BadUserCertificateException
    {

    }
    class NoNeighborsForRoutingException: ApplicationException
    {

    }
    class ExpiredUserKeysException: BadSignatureException
    {

    }
    class NonUniqueNonceException : PossibleMitmException
    {

    }





    class DrpTimeoutException : ApplicationException // next hop or EP, or whatever responder timed out
    {
        public DrpTimeoutException(string message = "Timeout while waiting for response")
            : base(message)
        {

        }

    }
    class NextHopRejectedException : ApplicationException
    {
        public NextHopRejectedException(NextHopResponseCode responseCode)
            : base($"Next hop rejected request with status = {responseCode}")
        {

        }
    }
    class NextHopRejectedExceptionServiceUnavailable : NextHopRejectedException
    {
        public NextHopRejectedExceptionServiceUnavailable() : base(NextHopResponseCode.rejected_serviceUnavailable)
        { }
    }

    class Pow1RejectedException : ApplicationException
    {
        public Pow1RejectedException(RegisterPow1ResponseStatusCode responseCode)
            : base($"EP rejected PoW1 request with status = {responseCode}")
        {

        }
    }


    class DrpResponderRejectedException : ApplicationException
    {
        internal static DrpResponderRejectedException Create(DrpResponderStatusCode responseCode)
        {
            if (responseCode == DrpResponderStatusCode.rejected_maxhopsReached) return new DrpResponderRejectedMaxHopsReachedException();
            else return new DrpResponderRejectedException(responseCode);
        }
        internal DrpResponderRejectedException(DrpResponderStatusCode responseCode)
            : base($"Responder rejected request with status = {responseCode}")
        {
        }
    }
    class DrpResponderRejectedMaxHopsReachedException : DrpResponderRejectedException
    {
        public DrpResponderRejectedMaxHopsReachedException(): base(DrpResponderStatusCode.rejected_maxhopsReached)
        {
        }
    }

}
