using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Ptarmigan.Models
{
    public class ConnectRequest
    {
        [JsonProperty("peerNodeId")] public string PeerNodeId { get; set; }
        [JsonProperty("peerAddr")] public string PeerAddr { get; set; }
        [JsonProperty("peerPort")] public int? PeerPort { get; set; }
    }
}