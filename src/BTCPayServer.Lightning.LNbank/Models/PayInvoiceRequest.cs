namespace BTCPayServer.Lightning.LNbank.Models
{
    public class PayInvoiceRequest
    {
        public string PaymentRequest { get; set; }
        public float? MaxFeePercent { get; set; }
    }
}
