namespace BTCPayServer.Lightning.Eclair.Models
{
    public class SendToNodeRequest
    {
        public string NodeId { get; set; }
        public long? AmountMsat { get; set; }
        public string PaymentHash { get; set; }
        public int? MaxAttempts { get; set; }
        public int? MaxFeePct { get; set; }
        public long? MaxFeeFlatSat { get; set; }
    }
}
