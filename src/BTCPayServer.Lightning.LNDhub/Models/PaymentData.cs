using System;
using BTCPayServer.Lightning.JsonConverters;
using BTCPayServer.Lightning.LNDhub.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class PaymentData
    {
        [JsonProperty("destination")]
        public string Destination { get; set; }
        
        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("description_hash")]
        public string DescriptionHash { get; set; }

        [JsonProperty("payment_hash")]
        [JsonConverter(typeof(LndHubBufferJsonConverter))]
        public string PaymentHash { get; set; }
        
        [JsonProperty("payment_preimage")]
        [JsonConverter(typeof(LndHubBufferJsonConverter))]
        public string PaymentPreimage { get; set; }
        
        [JsonProperty("payment_addr")]
        [JsonConverter(typeof(LndHubBufferJsonConverter))]
        public string PaymentAddress { get; set; }
        
        [JsonProperty("payment_error")]
        public string PaymentError { get; set; }

        [JsonProperty("num_satoshis")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }

        [JsonProperty("num_msat")]
        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney AmountMsat { get; set; }

        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }

        [JsonProperty("expiry")]
        [JsonConverter(typeof(LndHubTimeSpanJsonConverter))]
        public TimeSpan ExpireTime { get; set; }
        
        [JsonProperty("decoded")]
        public PaymentData Decoded { get; set; }
        
        [JsonProperty("payment_route")]
        public PaymentRoute PaymentRoute { get; set; }
    }
}
