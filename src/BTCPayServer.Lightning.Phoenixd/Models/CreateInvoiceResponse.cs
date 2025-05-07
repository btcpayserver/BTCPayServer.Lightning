using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Phoenixd.Models
{
    public class CreateInvoiceResponse
    {
        [JsonProperty("amountSat")]
        public long AmountSat { get; set; }

        [JsonProperty("paymentHash")]
        public string PaymentHash { get; set; }

        [JsonProperty("serialized")]
        public string Serialized { get; set; }
    }
}
