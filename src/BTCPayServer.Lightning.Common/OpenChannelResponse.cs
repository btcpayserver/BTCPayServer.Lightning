using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Lightning
{
    public enum OpenChannelResult
    {
        Ok,
        CannotAffordFunding,
        PeerNotConnected,
        NeedMoreConf,
        AlreadyExists,
    }

    public class OpenChannelResponse
    {
        public OpenChannelResponse(OpenChannelResult result)
        {
            Result = result;
        }
        public OpenChannelResult Result
        {
            get; set;
        }
        public string FundingTxIdIfAvailable
        {
            get; set;
        }
    }
}
