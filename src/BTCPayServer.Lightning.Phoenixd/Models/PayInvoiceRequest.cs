using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Phoenixd.Models
{
    public class PayInvoiceRequest
    {
        [JsonProperty("amountSat")]
        public long? AmountSat { get; set; }

        [JsonProperty("invoice")]
        public string Invoice { get; set; }
    }
}
