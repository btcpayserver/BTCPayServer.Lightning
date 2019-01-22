using System.Collections.Generic;

namespace BTCPayServer.Lightning.Eclair.Models
{
    public class SendResponse
    {
        public class LastUpdate
        {
            public string signature { get; set; }
            public string chainHash { get; set; }
            public string shortChannelId { get; set; }
            public int timestamp { get; set; }
            public int messageFlags { get; set; }
            public int channelFlags { get; set; }
            public int cltvExpiryDelta { get; set; }
            public int htlcMinimumMsat { get; set; }
            public int feeBaseMsat { get; set; }
            public int feeProportionalMillionths { get; set; }
            public int htlcMaximumMsat { get; set; }
        }

        public class Route
        {
            public string nodeId { get; set; }
            public string nextNodeId { get; set; }
            public LastUpdate lastUpdate { get; set; }
        }

        public class SendFailureResponseItem
        {
            public string t { get; set; }
        }

        public int amountMsat { get; set; }
        public string paymentHash { get; set; }
        public string paymentPreimage { get; set; }
        public List<SendFailureResponseItem> failures { get; set; }
        public List<Route> route { get; set; }

    }
}