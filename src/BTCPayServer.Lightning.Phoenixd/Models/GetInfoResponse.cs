using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Phoenixd.Models;

public class GetInfoResponse
{
    [JsonProperty("nodeId")]
    public string NodeId { get; set; }

    [JsonProperty("channels")]
    public GetInfoChannel[] Channels { get; set; }

    [JsonProperty("chain")]
    public string Chain { get; set; }

    [JsonProperty("blockHeight")]
    public int BlockHeight { get; set; }

    [JsonProperty("version")]
    public string Version { get; set; }
}

public class GetInfoChannel
{
    [JsonProperty("state")]
    public string State { get; set; }
}
