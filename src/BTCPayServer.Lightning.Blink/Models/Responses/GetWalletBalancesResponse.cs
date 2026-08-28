namespace BTCPayServer.Lightning.Blink.Models.Responses
{
    public class GetWalletBalancesResponse
    {
        public MeResponse Me { get; set; }
        public RealtimePriceResponse RealtimePrice { get; set; }
    }
}

