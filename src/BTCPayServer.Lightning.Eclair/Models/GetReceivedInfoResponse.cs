using System;
using BTCPayServer.Lightning.Eclair.JsonConverters;
using BTCPayServer.Lightning.JsonConverters;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Eclair.Models
{
    public class GetReceivedInfoResponse
    {
        public class InfoStatus
        {
            public string Type { get; set; }
            [JsonConverter(typeof(LightMoneyJsonConverter))]
            public LightMoney Amount { get; set; }
            [JsonConverter(typeof(EclairDateTimeJsonConverter))]
            public DateTimeOffset ReceivedAt { get; set; }
        }
        public class InfoPaymentRequest
        {
            public string Prefix { get; set; }
            [JsonConverter(typeof(EclairDateTimeJsonConverter))]
            public DateTimeOffset Timestamp { get; set; }
            public string NodeId { get; set; }
            public string Serialized { get; set; }
            public string Description { get; set; }
            public string paymentHash { get; set; }
            [JsonConverter(typeof(EclairDateTimeJsonConverter))]
            public DateTimeOffset Expiry { get; set; }
            [JsonConverter(typeof(LightMoneyJsonConverter))]
            public LightMoney Amount { get; set; }
        }
        public InfoPaymentRequest PaymentRequest { get; set; }
        public string PaymentPreimage { get; set; }
        [JsonConverter(typeof(EclairDateTimeJsonConverter))]
        public DateTimeOffset CreatedAt { get; set; }
        public InfoStatus Status { get; set; }
    }
}
