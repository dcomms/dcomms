using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms
{
    class InsufficientResourcesException : ApplicationException
    {

    }
    class BrokenCipherException : ApplicationException // todo handle attacks that raise such exceptions
    {

    }
    class UnmatchedResponseFieldsException : ApplicationException // todo handle attacks that raise such exceptions   - it is MITM
    {

    }
    class BadSignatureException : ApplicationException // todo handle attacks that raise such exceptions   - it is MITM
    {

    }
}
