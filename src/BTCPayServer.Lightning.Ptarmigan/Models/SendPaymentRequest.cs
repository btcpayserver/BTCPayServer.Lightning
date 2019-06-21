using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class SendPaymentRequest
    {
        [JsonProperty("bolt11")] public string Bolt11 { get; set; }
        [JsonProperty("addAmountMsat")] public int? AddAmountMsat { get; set; }
    }
}