namespace BTCPayServer.Lightning.Eclair.Models
{
    public partial class GetReceivedInfoResponse
    {
        public string PaymentHash { get; set; }
        public long AmountMsat { get; set; }
        public long ReceivedAt { get; set; }
    }
}