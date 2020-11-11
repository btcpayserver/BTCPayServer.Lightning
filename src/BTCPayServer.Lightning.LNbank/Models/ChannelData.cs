using BTCPayServer.Lightning.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNbank.Models
{
    public class ChannelData
    {
        public string RemoteNode { get; set; }

        public bool IsPublic { get; set; }

        public bool IsActive { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney Capacity { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney LocalBalance { get; set; }

        public string ChannelPoint { get; set; }
    }
}
