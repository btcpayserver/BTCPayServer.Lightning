using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class SendPaymentResponse
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("error")] public ErrorResponse Error { get; set; }
        [JsonProperty("result")] public SendPaymentResultResponse Result { get; set; }
    }
}