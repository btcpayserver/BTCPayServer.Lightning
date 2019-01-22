namespace BTCPayServer.Lightning.Eclair.Models
{
    public class CheckInvoiceResponse
    {
        public string prefix { get; set; }
        public object amount { get; set; }
        public int timestamp { get; set; }
        public string nodeId { get; set; }
        public string description { get; set; }
        public string paymentHash { get; set; }
        public int expiry { get; set; }
        public object minFinalCltvExpiry { get; set; }
    }
}