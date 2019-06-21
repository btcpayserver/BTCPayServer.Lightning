using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class InvoiceResponse
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("result")] public InvoiceResultResponse Result { get; set; }
    }
}