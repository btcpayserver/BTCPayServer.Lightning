using Newtonsoft.Json;

namespace BTCPayServer.Lightning.CLightning
{
    public class CLightningChannel
    {
        public string Source { get; set; }
        public string Destination { get; set; }

        [JsonProperty("satoshis")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney Capacity { get; set; }
        public bool Active { get; set; }
    }
}
