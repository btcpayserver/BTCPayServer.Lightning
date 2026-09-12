namespace BTCPayServer.Lightning.Blink.Models.Responses
{
    public class RealtimePriceResponse
    {
        public PriceInfo BtcSatPrice { get; set; }

        public string DenominatorCurrency { get; set; }
    }
}

