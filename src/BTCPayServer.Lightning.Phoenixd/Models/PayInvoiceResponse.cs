using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Phoenixd.Models
{
    public class PayInvoiceResponse
    {
        [JsonProperty("recipientAmountSat")]
        public long RecipientAmountSat { get; set; }

        [JsonProperty("routingFeeSat")]
        public long RoutingFeeSat { get; set; }

        [JsonProperty("paymentId")]
        public string PaymentId { get; set; }

        [JsonProperty("paymentHash")]
        public string PaymentHash { get; set; }

        [JsonProperty("paymentPreimage")]
        public string PaymentPreimage { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }
    }
}
