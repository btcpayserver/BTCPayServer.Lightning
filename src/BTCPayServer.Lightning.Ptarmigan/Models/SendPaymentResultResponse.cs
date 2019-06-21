using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class SendPaymentResultResponse
    {
        [JsonProperty("payment_id")] public int PaymentId { get; set; }
    }
}