namespace BTCPayServer.Lightning.Blink.Models.Responses
{
    public class InvoiceResponse
    {
        public string PaymentRequest { get; set; }
        public string PaymentHash { get; set; }
        public string PaymentSecret { get; set; }
        public long Satoshis { get; set; }
    }
}

