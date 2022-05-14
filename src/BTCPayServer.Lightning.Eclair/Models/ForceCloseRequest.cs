namespace BTCPayServer.Lightning.Eclair.Models
{
    public class ForceCloseRequest
    {
        public string ChannelId { get; set; }
        public string ShortChannelId { get; set; }
    }
}
