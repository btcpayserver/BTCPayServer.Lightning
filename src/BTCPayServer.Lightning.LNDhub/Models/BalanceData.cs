using BTCPayServer.Lightning.LNDhub.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class BalanceData
    {
        [JsonProperty("btc")]
        public BTCBalance BtcBalance { get; set; }
    }

    public class BTCBalance
    {
        [JsonProperty("availableBalance")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney AvailableBalance { get; set; }
    }
}
