using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.CLightning
{
    public class PeerChannel
    {
        [JsonProperty("peer_id")]
        public string PeerId { get; set; }
        public bool Private { get; set; }

        [JsonProperty("to_us_msat")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney ToUs
        {
            get;
            set;
        }

        [JsonProperty("funding_txid")]
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        public uint256 FundingTxId { get; set; }

        [JsonProperty("short_channel_id")]
        [JsonConverter(typeof(JsonConverters.ShortChannelIdJsonConverter))]
        public ShortChannelId ShortChannelId { get; set; }

        [JsonProperty("total_msat")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney Total
        {
            get;
            set;
        }
        public string State { get; set; }
    }
}
