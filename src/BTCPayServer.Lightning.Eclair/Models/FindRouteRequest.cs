namespace BTCPayServer.Lightning.Eclair.Models
{
    public class FindRouteRequest
    {
        public string Invoice { get; set; }
        public int? AmountMsat { get; set; }
    }
}