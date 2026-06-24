using BTCPayServer.Lightning.LNDhub.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class BalanceData
    {
        [JsonProperty(PropertyName = "BTC")]
        public BtcBalance BTC { get; set; }
    }

    public class BtcBalance
    {
        [JsonProperty(PropertyName = "AvailableBalance")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney AvailableBalance { get; set; }
    }
}
