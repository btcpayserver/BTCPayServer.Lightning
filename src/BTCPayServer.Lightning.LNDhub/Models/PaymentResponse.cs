using System;
using System.Collections.Generic;
using BTCPayServer.Lightning.JsonConverters;
using BTCPayServer.Lightning.LNDhub.JsonConverters;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.Models
{
    public class PaymentResponse
    {
        [JsonProperty("payment_error")]
        public string PaymentError { get; set; }
        
        [JsonProperty("payment_preimage")]
        [JsonConverter(typeof(LndHubBufferJsonConverter))]
        public uint256 PaymentPreimage { get; set; }
        
        [JsonProperty("payment_route")]
        public PaymentRoute PaymentRoute { get; set; }

        [JsonProperty("payment_hash")]
        [JsonConverter(typeof(LndHubBufferJsonConverter))]
        public uint256 PaymentHash { get; set; }
        
        [JsonProperty("pay_req")]
        public string PaymentRequest { get; set; }
        
        [JsonProperty("decoded")]
        public PaymentData Decoded { get; set; }
    }
    
    public class PaymentRoute
    {
        [JsonProperty("hops")]
        public IEnumerable<PaymentRouteHop> Hops { get; set; }
        
        [JsonProperty("total_amt")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }
        
        [JsonProperty("total_fees")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney Fee { get; set; }

        [JsonProperty("total_amt_msat")]
        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney AmountMsat { get => Amount;  }

        [JsonProperty("total_fees_msat")]
        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney FeeMsat { get => Fee; }
    }
    
    public class PaymentRouteHop
    {
        [JsonProperty("chan_id")]
        public string ChannelId { get; set; }
        
        [JsonProperty("pub_key")]
        public string PubKey { get; set; }
        
        [JsonProperty("tlv_payload")]
        public bool TlvPayload { get; set; }
        
        [JsonProperty("chan_capacity")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney ChannelCapacity { get; set; }

        [JsonProperty("amt_to_forward")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney AmountToForward { get; set; }

        [JsonProperty("fee")]
        [JsonConverter(typeof(LndHubLightMoneyJsonConverter))]
        public LightMoney Fee { get; set; }

        [JsonProperty("amt_to_forward_msat")]
        public LightMoney AmountToForwardMsat { get => AmountToForward; }

        [JsonProperty("fee_msat")]
        public LightMoney FeeMsat { get => Fee; }
    }
}
