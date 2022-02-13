namespace BTCPayServer.Lightning.Eclair.Models
{
    public class GetSentInfoRequest
    {
        public string PaymentHash { get; set; }
        public string Id { get; set; }
    }
}
