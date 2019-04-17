namespace BTCPayServer.Lightning.Eclair.Models
{
    public class FindRouteToNodeRequest
    {
        public string NodeId { get; set; }
        public int AmountMsat { get; set; }
    }
}