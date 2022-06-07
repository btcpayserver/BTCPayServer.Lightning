using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class PayInvoiceRequest
    {
        [JsonProperty("invoice")]
        public string PaymentRequest { get; set; }

        // Amount in satoshis
        [JsonProperty("amount")]
        public long? Amount { get; set; }
	}
}
