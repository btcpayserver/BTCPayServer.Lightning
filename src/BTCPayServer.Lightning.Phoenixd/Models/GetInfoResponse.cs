using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Phoenixd.Models;

public class GetInfoResponse
{
    [JsonProperty("nodeId")]
    public string NodeId { get; set; }

    [JsonProperty("chain")]
    public string Chain { get; set; }

    [JsonProperty("blockHeight")]
    public int BlockHeight { get; set; }

    [JsonProperty("version")]
    public string Version { get; set; }
}
