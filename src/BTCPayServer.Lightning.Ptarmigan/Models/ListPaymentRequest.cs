using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class ListPaymentRequest
    {
        [JsonProperty("listpayment")] public int listPaymentId { get; set; }
    }
}