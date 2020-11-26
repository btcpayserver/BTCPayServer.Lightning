namespace BTCPayServer.Lightning.LNbank.Models
{
    public class TransactionUpdateEvent
    {
        public string TransactionId { get; set; }
        public string InvoiceId { get; set; }
        public string WalletId { get; set; }
        public string Status { get; set; }
        public string Event { get; set; }
        public bool IsPaid { get; set; }
        public bool IsExpired { get; set; }
    }
}
