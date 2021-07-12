namespace BTCPayServer.Lightning.LNbank.Models
{
    public class PayInvoiceRequest
    {
        public string WalletId { get; set; }

        public string PaymentRequest { get; set; }
    }
}
