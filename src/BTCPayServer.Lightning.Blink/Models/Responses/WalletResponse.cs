namespace BTCPayServer.Lightning.Blink.Models.Responses
{
    public class WalletResponse
    {
        public long Balance { get; set; }
        public string Id { get; set; }
        public string WalletCurrency { get; set; }
    }
}
