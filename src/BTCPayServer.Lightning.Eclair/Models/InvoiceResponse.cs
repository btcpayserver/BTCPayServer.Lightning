namespace BTCPayServer.Lightning.Eclair.Models
{
    public partial class InvoiceResponse
    {
        public string Prefix { get; set; }
        public long Timestamp { get; set; }
        public string NodeId { get; set; }
        public string Serialized { get; set; }
        public string Description { get; set; }
        public string PaymentHash { get; set; }
        public long Expiry { get; set; }
    }
}