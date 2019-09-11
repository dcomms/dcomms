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
    class UnmatchedFieldsException : PossibleMitmException // todo handle attacks that raise such exceptions   - it is MITM
    {

    }
    class BadSignatureException : PossibleMitmException // todo handle attacks that raise such exceptions   - it is MITM
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

}
