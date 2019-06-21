using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class OpenRequest
    {
        [JsonProperty("peerNodeId")] public string PeerNodeId { get; set; }
        [JsonProperty("fundingSat")] public long FundingSat { get; set; }
        [JsonProperty("pushMsat")] public int? PushMsat { get; set; }
        [JsonProperty("feeratePerKw")] public long? FeeratePerKw { get; set; }
    }
}