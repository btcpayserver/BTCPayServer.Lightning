using BTCPayServer.Lightning.JsonConverters;
using BTCPayServer.Lightning.LNDhub.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class PaymentRoute
    {
        [JsonProperty("total_amt")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }
        
        [JsonProperty("total_fees")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney Fee { get; set; }

        [JsonProperty("total_amt_msat")]
        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney AmountMsat { get; set; }

        [JsonProperty("total_fees_msat")]
        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney FeeMsat { get; set; }
    }
}
