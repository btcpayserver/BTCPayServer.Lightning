using System;

namespace BTCPayServer.Lightning
{
    public class CloseChannelRequest
    {
        public NodeInfo NodeInfo
        {
            get; set;
        }
        public string ChannelPointFundingTxIdStr
        {
            get; set;
        }
        public long ChannelPointOutputIndex
        {
            get; set;
        }
        public static void AssertIsSane(CloseChannelRequest closeChannelRequest)
        {
            if (closeChannelRequest == null)
                throw new ArgumentNullException(nameof(closeChannelRequest));
            if (closeChannelRequest.ChannelPointFundingTxIdStr == null)
                throw new ArgumentNullException(nameof(closeChannelRequest.ChannelPointFundingTxIdStr));
        }
    }
}
