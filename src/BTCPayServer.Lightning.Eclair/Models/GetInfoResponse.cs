using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Eclair.Models;

public class GetInfoResponse
{
    [JsonProperty("nodeId")]
    public string NodeId { get; set; }
    
    [JsonProperty("instanceId")]
    public string InstanceId { get; set; }
    
    [JsonProperty("alias")]
    public string Alias { get; set; }
    
    [JsonProperty("color")]
    public string Color { get; set; }
    
    [JsonProperty("version")]
    public string Version { get; set; }
    
    [JsonProperty("chainHash")]
    public string ChainHash { get; set; }
    
    [JsonProperty("network")]
    public string Network { get; set; }
    
    [JsonProperty("blockHeight")]
    public int BlockHeight { get; set; }
    
    [JsonProperty("publicAddresses")]
    public List<string> PublicAddresses { get; set; }
}
