namespace BTCPayServer.Lightning.Eclair.Models
{
    public class OpenRequest
    {
        public string NodeId { get; set; }
        public long FundingSatoshis { get; set; }
        public long? PushMsat { get; set; }
        public long? FundingFeerateSatByte { get; set; }
        public ChannelFlags? ChannelFlags { get; set; }
    }
}
