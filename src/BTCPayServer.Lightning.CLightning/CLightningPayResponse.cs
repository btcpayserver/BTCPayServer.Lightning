using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.CLightning
{
    public class CLightningPayResponse
    {
        public string Destination { get; set; }

        public string Status { get; set; }
        public int Parts { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        [JsonProperty("payment_hash")]
        public uint256 PaymentHash { get; set; }

        [JsonProperty("msatoshi")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }

        [JsonProperty("msatoshi_sent")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney AmountSent { get; set; }
    }
}
