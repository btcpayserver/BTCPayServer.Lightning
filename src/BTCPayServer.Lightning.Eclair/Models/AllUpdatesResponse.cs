namespace BTCPayServer.Lightning.Eclair.Models
{
    public partial class AllUpdatesResponse
    {
        public string Signature { get; set; }
        public string ChainHash { get; set; }
        public string ShortChannelId { get; set; }
        public long Timestamp { get; set; }
        public long MessageFlags { get; set; }
        public long ChannelFlags { get; set; }
        public long CltvExpiryDelta { get; set; }
        public long HtlcMinimumMsat { get; set; }
        public long FeeBaseMsat { get; set; }
        public long FeeProportionalMillionths { get; set; }
        public long HtlcMaximumMsat { get; set; }
    }
}
