using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNbank.Models
{
    public class CreateChannelRequest
    {
        [JsonProperty("nodeURI")]
        public string NodeURI { get; set; }

        [JsonConverter(typeof(MoneyJsonConverter))]
        public Money ChannelAmount { get; set; }

        [JsonConverter(typeof(FeeRateJsonConverter))]
        public FeeRate FeeRate { get; set; }
    }
}
