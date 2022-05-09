namespace BTCPayServer.Lightning.Eclair.Models
{
    public class PayInvoiceRequest
    {
        public string Invoice { get; set; }
        public long? AmountMsat { get; set; }
        public int? MaxAttempts { get; set; }
        public int? MaxFeePct { get; set; }
        public long? MaxFeeFlatSat { get; set; }
    }
}
