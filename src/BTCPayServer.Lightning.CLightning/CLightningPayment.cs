using System;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.CLightning
{
    public class CLightningPayment
    {
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        [JsonProperty("payment_hash")]
        public uint256 PaymentHash { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        [JsonProperty("preimage")]
        public uint256 Preimage { get; set; }

        [JsonProperty("amount_msat")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney MilliSatoshi { get; set; }

        [JsonProperty("amount_sent_msat")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney MilliSatoshiSent { get; set; }

        [JsonProperty("bolt11")]
        public string BOLT11 { get; set; }

        [JsonProperty("bolt12")]
        public string BOLT12 { get; set; }

        public string Label { get; set; }

        public string Status { get; set; }

        [JsonProperty("created_at")]
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? CreatedAt { get; set; }
    }
}
