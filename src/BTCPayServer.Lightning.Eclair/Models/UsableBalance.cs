using BTCPayServer.Lightning.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Eclair.Models
{
    public class UsableBalance
    {
        [JsonProperty("remoteNodeId")]
        public string RemoteNodeId { get; set; }

        [JsonProperty("shortChannelId")]
        public string ShortChannelId { get; set; }
        
        [JsonProperty("canSend")]
        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney CanSend { get; set; }
        
        [JsonProperty("canReceive")]
        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney CanReceive { get; set; }

        [JsonProperty("isPublic")]
        public bool IsPublic { get; set; }
    }
}
