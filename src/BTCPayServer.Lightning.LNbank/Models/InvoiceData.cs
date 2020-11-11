using System;
using BTCPayServer.Lightning.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Lightning.LNbank.Models
{
    public class InvoiceData
    {
        public string Id { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public LightningInvoiceStatus Status { get; set; }

        [JsonProperty("BOLT11")]
        public string BOLT11 { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? PaidAt { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset ExpiresAt { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney AmountReceived { get; set; }
    }
}
