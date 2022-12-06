using System.Collections.Generic;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.CLightning
{
    public class CLightningPayResponse
    {
        public string Destination { get; set; }

        public string Status { get; set; }
        public int Parts { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        [JsonProperty("payment_hash")]
        public uint256 PaymentHash { get; set; }        
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        [JsonProperty("payment_preimage")]
        public uint256 PaymentPreImage { get; set; }

        [JsonProperty("amount_msat")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }

        [JsonProperty("amount_sent_msat")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney AmountSent { get; set; }

#pragma warning disable IDE0051
        // Legacy stuff
        [JsonProperty("msatoshi_sent")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        LightMoney msatoshi_sent { set { AmountSent = value; } }

        [JsonProperty("msatoshi")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        LightMoney msatoshi { set { Amount = value; } }
#pragma warning restore IDE0051

        [Newtonsoft.Json.JsonExtensionData]
        public IDictionary<string, JToken> AdditionalProperties { get; set; }
    }
}
