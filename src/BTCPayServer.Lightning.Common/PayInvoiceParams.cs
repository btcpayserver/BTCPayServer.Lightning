namespace BTCPayServer.Lightning
{
    public class PayInvoiceParams
    {
        public PayInvoiceParams()
        {
        }

        public PayInvoiceParams(float maxFeePercent)
        {
            MaxFeePercent = maxFeePercent;
        }

        public float? MaxFeePercent { get; set; }
    }
}
