namespace BTCPayServer.Lightning.Eclair.Models
{
    public partial class PaymentRelayedEvent
    {
        public string Type { get; set; }
        public long AmountIn { get; set; }
        public long AmountOut { get; set; }
        public string PaymentHash { get; set; }
        public string FromChannelId { get; set; }
        public string ToChannelId { get; set; }
        public long Timestamp { get; set; }
    }
}