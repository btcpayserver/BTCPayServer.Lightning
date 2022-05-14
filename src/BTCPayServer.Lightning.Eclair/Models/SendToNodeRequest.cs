namespace BTCPayServer.Lightning.Eclair.Models
{
    public class SendToNodeRequest
    {
        public string NodeId { get; set; }
        public int AmountMsat { get; set; }
        public string PaymentHash { get; set; }
        public int? MaxAttempts { get; set; }
    }
}
