namespace BTCPayServer.Lightning.Eclair.Models
{
    public class CloseRequest
    {
        public string ChannelId { get; set; }
        public string ShortChannelId { get; set; }
        public string ScriptPubKey { get; set; }
    }
}
