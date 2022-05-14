namespace BTCPayServer.Lightning.Eclair.Models
{
    public partial class PaymentSentEvent
    {
        public string Type { get; set; }
        public long Amount { get; set; }
        public long FeesPaid { get; set; }
        public string PaymentHash { get; set; }
        public string PaymentPreimage { get; set; }
        public string ToChannelId { get; set; }
        public long Timestamp { get; set; }
    }
}
