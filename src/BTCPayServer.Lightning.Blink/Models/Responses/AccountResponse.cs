namespace BTCPayServer.Lightning.Blink.Models.Responses
{
    public class AccountResponse
    {
        public string DefaultWalletId { get; set; }
        public List<WalletResponse> Wallets { get; set; }
    }
}

