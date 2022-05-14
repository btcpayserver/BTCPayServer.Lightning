namespace BTCPayServer.Lightning.LNbank.Models
{
    public class PayInvoiceRequest
    {
        public string PaymentRequest { get; set; }
        public double? MaxFeePercent { get; set; }
        public long? MaxFeeFlat { get; set; }
    }
}
