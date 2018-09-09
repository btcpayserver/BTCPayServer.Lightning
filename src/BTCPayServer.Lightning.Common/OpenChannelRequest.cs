using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Lightning
{
    public class OpenChannelRequest
    {
        public NodeInfo NodeInfo
        {
            get; set;
        }
        public Money ChannelAmount
        {
            get; set;
        }
        public FeeRate FeeRate
        {
            get; set;
        }
        public static void AssertIsSane(OpenChannelRequest openChannelRequest)
        {
            if(openChannelRequest == null)
                throw new ArgumentNullException(nameof(openChannelRequest));
            if(openChannelRequest.ChannelAmount == null)
                throw new ArgumentNullException(nameof(openChannelRequest.ChannelAmount));
            if(openChannelRequest.NodeInfo == null)
                throw new ArgumentNullException(nameof(openChannelRequest.NodeInfo));
        }
    }
}
