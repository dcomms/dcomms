using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.DMP
{
    class MessageSession
    {
        public uint MessageId32;
        byte[] AesKey;
        byte[] MessagePlainText;
        byte[] MessageEncodedAesCbc;
        byte[] HmacKey;

        public MessageSessionStatusCode Status;
    }

    enum MessageSessionStatusCode
    {
        inProgress = 1,
        canceled = 2,
        finishedSuccessfully = 3,
    }
}
