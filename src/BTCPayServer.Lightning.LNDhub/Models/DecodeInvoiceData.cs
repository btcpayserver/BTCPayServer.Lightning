using System;
using BTCPayServer.Lightning.JsonConverters;
using BTCPayServer.Lightning.LNDhub.JsonConverters;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class DecodeInvoiceData
    {
        [JsonProperty(PropertyName = "destination")]
        public string Destination { get; set; }

        [JsonProperty("payment_hash")]
        public string PaymentHash { get; set; }

        [JsonProperty("num_satoshis")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }

        [JsonProperty("num_msat")]
        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney AmountMsat { get => Amount; }
    
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? Timestamp { get; set; }

        [JsonProperty("expiry")]
        [JsonConverter(typeof(LndHubTimeSpanJsonConverter))]
        public TimeSpan Expiry { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    
        [JsonProperty("description_hash", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        public uint256 DescriptionHash { get; set; }
    }
}
