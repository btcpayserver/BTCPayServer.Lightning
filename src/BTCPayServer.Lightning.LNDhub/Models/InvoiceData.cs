using System;
using BTCPayServer.Lightning.LNDhub.JsonConverters;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class InvoiceData
    {
        [JsonProperty("r_hash")]
        [JsonConverter(typeof(LndHubBufferJsonConverter))]
        public uint256 Id { get; set; }
    
        [JsonProperty("type")]
        public string Type { get => "user_invoice"; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("add_index")]
        public int AddIndex { get; set; }

        [JsonProperty("payment_hash")]
        public string PaymentHash { get; set; }

        [JsonProperty("payment_request")]
        public string PaymentRequest { get; set; }

        [JsonProperty("pay_req")]
        public string PayReq { get => PaymentRequest; }

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
