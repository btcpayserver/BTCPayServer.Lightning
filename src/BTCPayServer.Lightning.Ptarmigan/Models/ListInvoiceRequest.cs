using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class ListInvoiceRequest
    {
        [JsonProperty("paymentHash")] public string PaymentHash { get; set; }
    }
}
