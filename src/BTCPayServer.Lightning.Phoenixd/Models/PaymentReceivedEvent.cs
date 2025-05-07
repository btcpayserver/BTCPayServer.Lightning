using System;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Phoenixd.Models
{
    public partial class PaymentReceivedEvent
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("amountSat")]
        public long Amount { get; set; }

        [JsonProperty("paymentHash")]
        public string PaymentHash { get; set; }

        [JsonProperty("timestamp")]
        public long Timestamp { get; set; }
    }
}
