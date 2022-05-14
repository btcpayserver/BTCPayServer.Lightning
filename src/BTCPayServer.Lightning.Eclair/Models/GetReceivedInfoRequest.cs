namespace BTCPayServer.Lightning.Eclair.Models
{
    public class GetReceivedInfoRequest
    {
        public string PaymentHash { get; set; }
        public string Invoice { get; set; }
    }
}
