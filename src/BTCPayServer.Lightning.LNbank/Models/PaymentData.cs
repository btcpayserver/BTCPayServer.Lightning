using System;
using BTCPayServer.Lightning.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Lightning.LNbank.Models
{
    public class PaymentData
    {
        public string Id { get; set; }
        public string Preimage { get; set; }
        public string PaymentHash { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public LightningPaymentStatus Status { get; set; }

        [JsonProperty("BOLT11")]
        public string BOLT11 { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? CreatedAt { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney FeeAmount { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney TotalAmount { get; set; }
    }
}
