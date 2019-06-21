using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class InvoiceResultResponse
    {
        [JsonProperty("hash")] public string Hash { get; set; }
        [JsonProperty("amount_msat")] public int AmountMsat { get; set; }
        [JsonProperty("bolt11")] public string Bolt11 { get; set; }
        [JsonProperty("note")] public string Note { get; set; }
    }
}
