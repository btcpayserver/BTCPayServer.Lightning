using System;
using BTCPayServer.Lightning.JsonConverters;
using BTCPayServer.Lightning.LNDhub.JsonConverters;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class PaymentData
    {
        [JsonProperty("payment_preimage")]
        [JsonConverter(typeof(LndHubBufferJsonConverter))]
        public uint256 PaymentPreimage { get; set; }
        
        [JsonProperty("destination")]
        public string Destination { get; set; }

        [JsonProperty("payment_hash")]
        [JsonConverter(typeof(LndHubBufferJsonConverter))]
        public uint256 PaymentHash { get; set; }

        [JsonProperty("num_satoshis")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }

        [JsonProperty("num_msat")]
        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney AmountMsat { get => Amount; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("description_hash", NullValueHandling = NullValueHandling.Ignore)]
        public string DescriptionHash { get; set; }

        [JsonProperty("fallback_addr", NullValueHandling = NullValueHandling.Ignore)]
        public string FallbackAddress { get; set; }

        [JsonProperty("cltv_expiry", NullValueHandling = NullValueHandling.Ignore)]
        public string CltvExpiry { get; set; }
        
        [JsonProperty("payment_addr", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(LndHubBufferJsonConverter))]
        public uint256 PaymentAddress { get; set; }

        [JsonProperty("expiry", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(LndHubTimeSpanJsonConverter))]
        public TimeSpan? ExpireTime { get; set; }

        [JsonProperty("timestamp", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(LndHubDateTimeOffsetConverter))]
        public DateTimeOffset? Timestamp { get; set; }
    }
}
