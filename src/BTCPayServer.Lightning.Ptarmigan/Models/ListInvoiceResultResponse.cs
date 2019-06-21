using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class ListInvoiceResultResponse
    {
        [JsonProperty("state")] public string State { get; set; }
        [JsonProperty("hash")] public string Hash { get; set; }
        [JsonProperty("amount_msat")] public int AmountMsat { get; set; }
        [JsonProperty("creation_time")] public string CreationTime { get; set; }
        [JsonProperty("expiry")] public int Expiry { get; set; }
        [JsonProperty("bolt11")] public string Bolt11 { get; set; }
    }
}
