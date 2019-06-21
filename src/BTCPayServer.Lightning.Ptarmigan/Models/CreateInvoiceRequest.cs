using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class CreateInvoiceRequest
    {
        [JsonProperty("amountMsat")] public long AmountMsat { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("invoiceExpiry")] public int InvoiceExpiry { get; set; }
    }
}