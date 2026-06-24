using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class CheckPaymentResponse
    {
        [JsonProperty(PropertyName = "paid")]
        public bool Paid { get; set; }
    }
}
