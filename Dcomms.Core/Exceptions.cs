using Dcomms.DRP.Packets;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms
{
    class InsufficientResourcesException : ApplicationException
    {

    }
    class PossibleAttackException : ApplicationException
    {

    }
    class BrokenCipherException : PossibleAttackException // todo handle attacks that raise such exceptions
    {

    }
    class UnmatchedFieldsException : PossibleAttackException 
    {

    }
    class BadSignatureException : PossibleAttackException 
    {

    }
    class BadUserCertificateException : BadSignatureException
    {

    }
    class CertificateOutOfDateException : BadUserCertificateException
    {

    }
   
    class ExpiredUserKeysException: BadSignatureException
    {

    }
    class NonUniqueNonceException : PossibleAttackException
    {

    }


    class RequestFailedException: ApplicationException
    {
        public RequestFailedException(string desc): base(desc)
        {
        }
    }


    class DrpTimeoutException : RequestFailedException // next hop or EP, or whatever responder timed out
    {
        public DrpTimeoutException(string message = "Timeout while waiting for response")
            : base(message)
        {

        }

    }

    class RequestRejectedException : RequestFailedException
    {
        public readonly ResponseOrFailureCode ResponseCode;
        public RequestRejectedException(ResponseOrFailureCode responseCode)
            : base($"Request was rejected with status = {responseCode}")
        {
            ResponseCode = responseCode;
        }
    }
  

    class NoNeighborsToSendInviteException : RequestFailedException
    {
        public NoNeighborsToSendInviteException()
            : base("no neighbors to send INVITE")
        {

        }
    }


    class Pow1RejectedException : ApplicationException
    {
        public Pow1RejectedException(RegisterPow1ResponseStatusCode responseCode)
            : base($"EP rejected PoW1 request with status = {responseCode}")
        {

        }
    }


    //class DrpResponderRejectedException : ApplicationException
    //{
    //    internal static DrpResponderRejectedException Create(DrpResponderStatusCode responseCode)
    //    {
    //        if (responseCode == DrpResponderStatusCode.rejected_maxhopsReached) return new DrpResponderRejectedMaxHopsReachedException();
    //        else if (responseCode == DrpResponderStatusCode.rejected_p2pNetworkServiceUnavailable) return new DrpResponderRejectedP2pNetworkServiceUnavailableException();
    //        else return new DrpResponderRejectedException(responseCode);
    //    }
    //    internal DrpResponderRejectedException(DrpResponderStatusCode responseCode)
    //        : base($"Responder rejected request with status = {responseCode}")
    //    {
    //    }
    //}
    //class DrpResponderRejectedMaxHopsReachedException : DrpResponderRejectedException
    //{
    //    public DrpResponderRejectedMaxHopsReachedException() : base(DrpResponderStatusCode.rejected_maxhopsReached)
    //    {
    //    }
    //}
    //class DrpResponderRejectedP2pNetworkServiceUnavailableException : DrpResponderRejectedException
    //{
    //    public DrpResponderRejectedP2pNetworkServiceUnavailableException() : base(DrpResponderStatusCode.rejected_p2pNetworkServiceUnavailable)
    //    {
    //    }
    //}

}
