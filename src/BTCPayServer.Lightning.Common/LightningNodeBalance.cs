namespace BTCPayServer.Lightning
{
    public class LightningNodeBalance
    {
        public OnchainBalance OnchainBalance { get; set; }
        public OffchainBalance OffchainBalance { get; set; }

        public LightningNodeBalance(OnchainBalance onchain, OffchainBalance offchain)
        {
            OnchainBalance = onchain;
            OffchainBalance = offchain;
        }
    }

    public class OnchainBalance
    {
        public LightMoney Confirmed { get; set; }
        public LightMoney Unconfirmed { get; set; }
        public LightMoney Reserved { get; set; }
    }

    public class OffchainBalance
    {
        public LightMoney Opening { get; set; }
        public LightMoney Local { get; set; }
        public LightMoney Remote { get; set; }
        public LightMoney Closing { get; set; }
    }
}
