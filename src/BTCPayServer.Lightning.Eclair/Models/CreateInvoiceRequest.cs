namespace BTCPayServer.Lightning.Eclair.Models
{
    public class CreateInvoiceRequest
    {
        public string Description { get; set; }
        public long? AmountMsat { get; set; }
        public int? ExpireIn { get; set; }
        public string FallbackAddress { get; set; }
    }
}