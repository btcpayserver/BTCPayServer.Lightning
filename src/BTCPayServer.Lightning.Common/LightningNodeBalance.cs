using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning
{
    public class LightningNodeBalance
    {
        public OnchainBalance OnchainBalance { get; set; }
        public OffchainBalance OffchainBalance { get; set; }
        
        // parameterless constructor for JSON serialization
        public LightningNodeBalance() {}

        public LightningNodeBalance(OnchainBalance onchain, OffchainBalance offchain)
        {
            OnchainBalance = onchain;
            OffchainBalance = offchain;
        }
    }

    public class OnchainBalance
    {
        [JsonConverter(typeof(NBitcoin.JsonConverters.MoneyJsonConverter))]
        public Money Confirmed { get; set; }
        
        [JsonConverter(typeof(NBitcoin.JsonConverters.MoneyJsonConverter))]
        public Money Unconfirmed { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.MoneyJsonConverter))]
        public Money Reserved { get; set; }
    }

    public class OffchainBalance
    {
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney Opening { get; set; }
        
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney Local { get; set; }
        
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney Remote { get; set; }
        
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney Closing { get; set; }
    }
}
