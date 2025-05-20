using System;
using BTCPayServer.Lightning.Phoenixd.JsonConverters;
using BTCPayServer.Lightning.JsonConverters;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Phoenixd.Models
{
    public class GetOutgoingPaymentResponse
    {
        [JsonProperty("paymentHash")]
        public string paymentHash { get; set; }

        [JsonProperty("preimage")]
        public string preImage { get; set; }

        [JsonProperty("isPaid")]
        public bool isPaid { get; set; }

        [JsonProperty("sent")]
        public long sent { get; set; }

        [JsonProperty("fees")]
        public long fees { get; set; }

        [JsonProperty("invoice")]
        public string invoice { get; set; }

        [JsonProperty("completedAt")]
        [JsonConverter(typeof(PhoenixdDateTimeJsonConverter))]
        public DateTimeOffset completedAt { get; set; }

        [JsonProperty("createdAt")]
        [JsonConverter(typeof(PhoenixdDateTimeJsonConverter))]
        public DateTimeOffset createdAt { get; set; }
    }
}
