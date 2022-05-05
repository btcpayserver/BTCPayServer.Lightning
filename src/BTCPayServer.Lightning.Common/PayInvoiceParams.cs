#nullable enable
namespace BTCPayServer.Lightning
{
    public class PayInvoiceParams
    {
        public double? MaxFeePercent { get; set; }
        public NBitcoin.Money? MaxFeeFlat { get; set; }
        public LightMoney? Amount { get; set; }
    }
}
