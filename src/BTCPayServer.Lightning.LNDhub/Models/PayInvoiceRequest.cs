using BTCPayServer.Lightning.LNDhub.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class PayInvoiceRequest
    {
        [JsonProperty("invoice")]
        public string PaymentRequest { get; set; }

        // Amount in satoshis
        [JsonProperty("amount")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }
	}
}
