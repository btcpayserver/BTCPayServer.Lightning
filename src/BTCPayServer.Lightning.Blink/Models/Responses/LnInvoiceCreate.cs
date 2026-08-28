namespace BTCPayServer.Lightning.Blink.Models.Responses
{
    public class LnInvoiceCreate
    {
        public InvoiceResponse Invoice { get; set; }
        public List<ErrorResponse> Errors { get; set; }
    }
}

