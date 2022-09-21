using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNbank.Models;

public class NodeInfoData
{
    public int BlockHeight { get; set; }

    [JsonProperty("nodeURIs")]
    public List<string> NodeURIs { get; set; }

    [JsonProperty(PropertyName = "alias", NullValueHandling = NullValueHandling.Ignore)]
    public string Alias { get; set; }

    [JsonProperty(PropertyName = "color", NullValueHandling = NullValueHandling.Ignore)]
    public string Color { get; set; }

    [JsonProperty(PropertyName = "version", NullValueHandling = NullValueHandling.Ignore)]
    public string Version { get; set; }

    [JsonProperty(PropertyName = "peersCount", NullValueHandling = NullValueHandling.Ignore)]
    public int PeersCount { get; set; }

    [JsonProperty(PropertyName = "inactiveChannelsCount", NullValueHandling = NullValueHandling.Ignore)]
    public int InactiveChannelsCount { get; set; }

    [JsonProperty(PropertyName = "pendingChannelsCount", NullValueHandling = NullValueHandling.Ignore)]
    public int PendingChannelsCount { get; set; }

    [JsonProperty(PropertyName = "activeChannelsCount", NullValueHandling = NullValueHandling.Ignore)]
    public int ActiveChannelsCount { get; set; }
}
