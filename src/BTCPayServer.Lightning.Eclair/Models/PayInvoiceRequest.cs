namespace BTCPayServer.Lightning.Eclair.Models
{
    public class PayInvoiceRequest
    {
        public string Invoice { get; set; }
        public int? AmountMsat { get; set; }
        public int? MaxAttempts { get; set; }
        public int? MaxFeePct { get; set; }
    }
}
