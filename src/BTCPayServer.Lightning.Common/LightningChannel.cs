using NBitcoin;

namespace BTCPayServer.Lightning
{
    public class LightningChannel
    {
        public string Id { get; set; }
        public PubKey RemoteNode { get; set; }
        public bool IsPublic { get; set; }
        public bool IsActive { get; set; }
        public LightMoney Capacity { get; set; }
        public LightMoney LocalBalance { get; set; }
        public OutPoint ChannelPoint { get; set; }
    }
}
