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
    class UnmatchedResponseFieldsException : PossibleMitmException // todo handle attacks that raise such exceptions   - it is MITM
    {

    }
    class BadSignatureException : PossibleMitmException // todo handle attacks that raise such exceptions   - it is MITM
    {

    }
}
