using System;
using BTCPayServer.Lightning.JsonConverters;
using BTCPayServer.Lightning.LNDhub.JsonConverters;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class TransactionData
    {
        private LightMoney _fee;
        private LightMoney _feeMsat;

        [JsonProperty("payment_hash")]
        [JsonConverter(typeof(LndHubBufferJsonConverter))]
        public uint256 PaymentHash { get; set; }
        
        [JsonProperty("payment_preimage")]
        public string PaymentPreimage { get; set; }

        [JsonProperty("type")]
        public string Type { get => "paid_invoice"; }
        
        [JsonProperty("memo")]
        public string Memo { get; set; }
        
        [JsonProperty("value")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney Value { get; set; }

        [JsonProperty("fee")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney Fee
        {
            get => _feeMsat ?? _fee;
            set => _fee = value;
        }

        [JsonProperty("fee_msat")]
        [JsonConverter(typeof(LightMoneyJsonConverter))]
        private LightMoney FeeMsat { set => _feeMsat = value; }
    
        [JsonConverter(typeof(LndHubDateTimeOffsetConverter))]
        public DateTimeOffset? Timestamp { get; set; }
    }
}
