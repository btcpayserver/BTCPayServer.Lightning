using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Phoenixd.Models
{
    public class SendPaymentRequest
    {
        [JsonProperty("amountSat")]
        public long? AmountSat { get; set; }

        [JsonProperty("address")]
        public string Address { get; set; }

        [JsonProperty("feerateSatByte")]
        public long? FeerateSatByte { get; set; }
    }
}
