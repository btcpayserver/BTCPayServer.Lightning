using System;
using BTCPayServer.Lightning.Eclair.JsonConverters;
using BTCPayServer.Lightning.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Eclair.Models
{
    public partial class GetSentInfoResponse
    {
        public Guid Id { get; set; }
        public Guid ParentId { get; set; }

        public string PaymentHash { get; set; }
        public string PaymentType { get; set; }
        public string RecipientNodeId { get; set; }
        public string Preimage { get; set; }
        public long AmountMsat { get; set; }
        [JsonConverter(typeof(EclairDateTimeJsonConverter))]
        public DateTimeOffset CreatedAt { get; set; }
        [JsonConverter(typeof(EclairDateTimeJsonConverter))]
        public DateTimeOffset CompletedAt { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney RecipientAmount { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public long FeesPaid { get; set; }

        public PaymentStatus Status { get; set; }
    }
}
