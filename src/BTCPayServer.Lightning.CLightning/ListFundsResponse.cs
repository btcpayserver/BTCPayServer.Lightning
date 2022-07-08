using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.CLightning
{
    public class ListFundsResponse
    {
        public FundsOutput[] Outputs { get; set; }
        public FundsChannel[] Channels { get; set; }
    }

    public class FundsOutput
    {
        public string Txid { get; set; }
        public int Output { get; set; }
        public string Address { get; set; }

        public string Status { get; set; }
        public bool Reserved { get; set; }
        public int Blockheight { get; set; }
        public int? ReservedToBlock { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.MoneyJsonConverter))]
        public Money Value { get; set; }
    }

    public class FundsChannel
    {
        [JsonProperty("peer_id")]
        public string PeerId { get; set; }
        
        [JsonProperty("funding_txid")]
        public string FundingTxid { get; set; }
        
        [JsonProperty("funding_output")]
        public int FundingOutput { get; set; }

        public string State { get; set; }
        public bool Connected { get; set; }

        [JsonProperty("our_amount_msat")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney LocalAmount { get; set; }

        [JsonProperty("amount_msat")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney TotalAmount { get; set; }

        [JsonProperty("short_channel_id")]
        [JsonConverter(typeof(JsonConverters.ShortChannelIdJsonConverter))]
        public ShortChannelId ShortChannelId { get; set; }
    }
}
