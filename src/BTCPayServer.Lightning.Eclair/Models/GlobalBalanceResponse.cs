using BTCPayServer.Lightning.Eclair.JsonConverters;
using BTCPayServer.Lightning.JsonConverters;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Eclair.Models
{
    public class GlobalBalanceResponse
    {
        [JsonProperty("total")]
        public decimal Total { get; set; }

        [JsonProperty("onChain")]
        public GlobalOnchainBalance Onchain { get; set; }

        [JsonProperty("offChain")]
        public GlobalOffchainBalance Offchain { get; set; }
    }

    public class GlobalOnchainBalance
    {
        [JsonProperty("confirmed")]
        public decimal Confirmed { get; set; }
        
        [JsonProperty("unconfirmed")]
        public decimal Unconfirmed { get; set; }
    }

    public class GlobalOffchainBalance
    {
        [JsonProperty("waitForFundingConfirmed")]
        [JsonConverter(typeof(EclairBtcJsonConverter))]
        public LightMoney WaitForFundingConfirmed { get; set; }
        
        [JsonProperty("waitForChannelReady")]
        [JsonConverter(typeof(EclairBtcJsonConverter))]
        public LightMoney WaitForChannelReady { get; set; }
        
        [JsonProperty("waitForPublishFutureCommitment")]
        [JsonConverter(typeof(EclairBtcJsonConverter))]
        public LightMoney WaitForPublishFutureCommitment { get; set; }
        
        [JsonProperty("negotiating")]
        [JsonConverter(typeof(EclairBtcJsonConverter))]
        public LightMoney Negotiating { get; set; }
        
        [JsonProperty("normal")]
        public EclairChannelBalance Normal { get; set; }
        
        [JsonProperty("shutdown")]
        public EclairChannelBalance Shutdown { get; set; }
        
        [JsonProperty("closing")]
        public EclairClosingBalances Closing { get; set; }
    }

    public class EclairClosingBalances
    {
        
        [JsonProperty("localCloseBalance")]
        public EclairChannelBalance LocalCloseBalance { get; set; }
        
        [JsonProperty("remoteCloseBalance")]
        public EclairChannelBalance RemoteCloseBalance { get; set; }
        
        [JsonProperty("mutualCloseBalance")]
        public EclairChannelBalance MutualCloseBalance { get; set; }
        
        [JsonProperty("unknownCloseBalance")]
        public EclairChannelBalance UnknownCloseBalance { get; set; }
    }

    public class EclairChannelBalance
    {
        [JsonProperty("toLocal")]
        [JsonConverter(typeof(EclairBtcJsonConverter))]
        public LightMoney ToLocal { get; set; }
    }
}
