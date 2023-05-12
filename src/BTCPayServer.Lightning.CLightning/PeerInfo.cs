using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.CLightning
{
    public class ChannelInfo
    {
        public string State { get; set; }
        public string Owner { get; set; }

        [JsonProperty("funding_txid")]
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        public uint256 FundingTxId { get; set; }

        [JsonProperty("short_channel_id")]
        [JsonConverter(typeof(JsonConverters.ShortChannelIdJsonConverter))]
        public ShortChannelId ShortChannelId { get; set; }

        [JsonProperty("msatoshi_to_us")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        [Obsolete("Use ToUs instead")]
        public LightMoney OldToUs { get; set; }

        [JsonProperty("msatoshi_total")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        [Obsolete("Use Total instead")]
        public LightMoney OldTotal { get; set; }

        LightMoney _ToUs;
        [JsonProperty("to_us_msat")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney ToUs
        {
            get
            {
#pragma warning disable CS0618 // Type or member is obsolete
                return _ToUs ?? OldToUs;
#pragma warning restore CS0618 // Type or member is obsolete
            }
            set
            {
                _ToUs = value;
            }
        }

        LightMoney _Total;
        [JsonProperty("total_msat")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney Total
        {
            get
            {
#pragma warning disable CS0618 // Type or member is obsolete
                return _Total ?? OldTotal;
#pragma warning restore CS0618 // Type or member is obsolete
            }
            set
            {
                _Total = value;
            }
        }

        [JsonProperty("dust_limit_satoshis")]
        [JsonConverter(typeof(NBitcoin.JsonConverters.MoneyJsonConverter))]
        public Money DustLimit { get; set; }

        [JsonProperty("max_htlc_value_in_flight_msat")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney MaxHTLCValueInFlight { get; set; }

        [JsonProperty("channel_reserve_satoshis")]
        [JsonConverter(typeof(NBitcoin.JsonConverters.MoneyJsonConverter))]
        public Money ChannelReserve { get; set; }

        [JsonProperty("htlc_minimum_msat")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney HTLCMinimum { get; set; }

        [JsonProperty("to_self_delay")]
        public int ToSelfDelay { get; set; }
        [JsonProperty("max_accepted_htlcs")]
        public int MaxAcceptedHTLCS { get; set; }

        public bool Private { get; set; }
        public string[] Status { get; set; }
    }
    public class PeerInfo
    {
        public string State { get; set; }
        public string Id { get; set; }
        [JsonProperty("netaddr")]
        public string[] NetworkAddresses { get; set; }
        public bool Connected { get; set; }
        public string Owner { get; set; }
        public ChannelInfo[] Channels { get; set; }

    }
}
