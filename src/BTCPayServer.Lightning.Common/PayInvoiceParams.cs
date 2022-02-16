#nullable enable
namespace BTCPayServer.Lightning
{
    public class PayInvoiceParams
    {
        public PayInvoiceParams()
        {
        }


        public double? MaxFeePercent { get; set; }
        public NBitcoin.Money? MaxFeeFlat { get; set; }
    }
}
