using System;
using System.ComponentModel;
using BTCPayServer.Lightning.LNDhub.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class InvoiceData
    {
        [JsonProperty("r_hash")]
        [JsonConverter(typeof(LndHubBufferJsonConverter))]
        public string Id { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("add_index")]
        public string AddIndex { get; set; }

        [JsonProperty("payment_hash")]
        public string PaymentHash { get; set; }

        [JsonProperty("payment_request")]
        public string PaymentRequest { get; set; }

        [JsonProperty("ispaid")]
        public bool IsPaid { get; set; }

        [JsonProperty("expire_time")]
        [JsonConverter(typeof(LndHubTimeSpanJsonConverter))]
        public TimeSpan ExpireTime { get; set; }

        [JsonProperty("amt")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }

        [JsonProperty("timestamp")]
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? CreatedAt { get; set; }
    }


}
