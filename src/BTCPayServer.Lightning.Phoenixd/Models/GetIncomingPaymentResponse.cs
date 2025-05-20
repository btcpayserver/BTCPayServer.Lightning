using System;
using BTCPayServer.Lightning.Phoenixd.JsonConverters;
using BTCPayServer.Lightning.JsonConverters;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Phoenixd.Models
{
    public class GetIncomingPaymentResponse
    {
        [JsonProperty("paymentHash")]
        public string PaymentHash { get; set; }

        [JsonProperty("preimage")]
        public string PreImage { get; set; }

        [JsonProperty("externalId")]
        public string ExternalId { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("invoice")]
        public string Invoice { get; set; }

        [JsonProperty("isPaid")]
        public bool IsPaid { get; set; }

        [JsonProperty("receivedSat")]
        public long ReceivedSat { get; set; }

        [JsonProperty("fees")]
        public long Fees { get; set; }

        [JsonProperty("completedAt")]
        [JsonConverter(typeof(PhoenixdDateTimeJsonConverter))]
        public DateTimeOffset CompletedAt { get; set; }

        [JsonProperty("createdAt")]
        [JsonConverter(typeof(PhoenixdDateTimeJsonConverter))]
        public DateTimeOffset CreatedAt { get; set; }
    }
}
