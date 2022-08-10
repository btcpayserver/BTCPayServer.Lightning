using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class NodeInfoData
    {
        [JsonProperty(PropertyName = "uris")]
        public IEnumerable<string> Uris { get; set; }
    
        [JsonProperty(PropertyName = "identity_pubkey")]
        public string IdentityPubkey { get; set; }
        
        [JsonProperty(PropertyName = "alias", NullValueHandling = NullValueHandling.Ignore)]
        public string Alias { get; set; }
        
        [JsonProperty(PropertyName = "version", NullValueHandling = NullValueHandling.Ignore)]
        public string Version { get; set; }

        [JsonProperty(PropertyName = "synced_to_chain", NullValueHandling = NullValueHandling.Ignore)]
        public bool? SyncedToChain { get; set; }
    
        [JsonProperty(PropertyName = "synced_to_graph", NullValueHandling = NullValueHandling.Ignore)]
        public bool? SyncedToGraph { get; set; }
    
        [JsonProperty(PropertyName = "testnet", NullValueHandling = NullValueHandling.Ignore)]
        public bool? IsTestnet { get; set; }
    
        [JsonProperty(PropertyName = "block_height", NullValueHandling = NullValueHandling.Ignore)]
        public int BlockHeight { get; set; }
    
        [JsonProperty(PropertyName = "block_hash", NullValueHandling = NullValueHandling.Ignore)]
        public string BlockHash { get; set; }
    
        [JsonProperty(PropertyName = "commit_hash", NullValueHandling = NullValueHandling.Ignore)]
        public string CommitHash { get; set; }
    
        [JsonProperty(PropertyName = "num_peers", NullValueHandling = NullValueHandling.Ignore)]
        public int PeersCount { get; set; }
    
        [JsonProperty(PropertyName = "num_inactive_channels", NullValueHandling = NullValueHandling.Ignore)]
        public int InactiveChannelsCount { get; set; }
    
        [JsonProperty(PropertyName = "num_pending_channels", NullValueHandling = NullValueHandling.Ignore)]
        public int PendingChannelsCount { get; set; }
    
        [JsonProperty(PropertyName = "num_active_channels", NullValueHandling = NullValueHandling.Ignore)]
        public int ActiveChannelsCount { get; set; }
    }
}
