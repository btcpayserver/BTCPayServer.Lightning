using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Phoenixd.Models
{
    public class CreateInvoiceRequest
    {
        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("amountSat")]
        public long? AmountSat { get; set; }

        [JsonProperty("expirySeconds")]
        public int? ExpirySeconds { get; set; }
    }
}
