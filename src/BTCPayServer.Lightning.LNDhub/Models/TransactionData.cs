using BTCPayServer.Lightning.LNDhub.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class TransactionData
    {
        [JsonProperty("payment_hash")]
        [JsonConverter(typeof(LndHubBufferJsonConverter))]
        public string PaymentHash { get; set; }
        
        [JsonProperty("payment_preimage")]
        public string PaymentPreimage { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }
        
        [JsonProperty("memo")]
        public string Memo { get; set; }
        
        [JsonProperty("value")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney Value { get; set; }

        [JsonProperty("fee")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney Fee { get; set; }

        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }
    }
}
