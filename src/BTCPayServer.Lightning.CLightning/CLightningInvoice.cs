using System;
using System.Collections.Generic;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.CLightning
{
    public class CLightningInvoice
    {
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        [JsonProperty("payment_hash")]
        public uint256 PaymentHash { get; set; }
        
        // this is used by the invoice endpoint
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        [JsonProperty("payment_secret")]
        public uint256 PaymentSecret { get; set; }
        
        // this is used by the waitanyinvoice and listinvoices endpoints
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        [JsonProperty("payment_preimage")]
        public uint256 PaymentPreimage { get; set; }

        [JsonProperty("amount_msat")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney MilliSatoshi { get; set; }

        [JsonProperty("amount_received_msat")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney MilliSatoshiReceived { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        [JsonProperty("expiry_time")]
        public DateTimeOffset ExpiryTime { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        [JsonProperty("expires_at")]
        public DateTimeOffset ExpiryAt { get; set; }
        [JsonProperty("bolt11")]
        public string BOLT11 { get; set; }
        [JsonProperty("pay_index")]
        public int? PayIndex { get; set; }
        public string Label { get; set; }
        public string Status { get; set; }
        [JsonProperty("paid_at")]
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? PaidAt { get; set; }

#pragma warning disable IDE0051
        // Legacy stuff
        [JsonProperty("msatoshi")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        LightMoney msatoshi { set { MilliSatoshi = value; } }

        [JsonProperty("msatoshi_received")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        LightMoney msatoshi_received { set { MilliSatoshiReceived = value; } }
#pragma warning restore IDE0051

        [Newtonsoft.Json.JsonExtensionData]
        public IDictionary<string, JToken> AdditionalProperties { get; set; }
    }
}
