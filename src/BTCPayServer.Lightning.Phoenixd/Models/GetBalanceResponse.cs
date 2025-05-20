using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Phoenixd.Models
{
    public class GetBalanceResponse
    {
        [JsonProperty("balanceSat")]
        public long balanceSat { get; set; }

        [JsonProperty("feeCreditSat")]
        public long feeCreditSat { get; set; }
    }
}
