using System;
using System.Collections.Generic;
using BTCPayServer.Lightning.Eclair.JsonConverters;
using BTCPayServer.Lightning.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Eclair.Models
{
    public class PaymentStatus
    {
        public string Type { get; set; }
        public string PaymentPreimage { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney FeesPaid { get; set; }
        
        [JsonConverter(typeof(EclairDateTimeJsonConverter))]
        public DateTimeOffset CompletedAt { get; set; }
        
        public List<PaymentRoutes> Route { get; set; }
        public List<PaymentFailures> Failures { get; set; }
    }

    public class PaymentRoutes
    {
        public string NodeId { get; set; }
        public string NextNodeId { get; set; }
        public string ShortChannelId { get; set; }
    }

    public class PaymentFailures
    {
        public string FailureType { get; set; }
        public string FailureMessage { get; set; }
        public string FailedNode { get; set; }
        public List<PaymentRoutes> FailedRoute { get; set; }
    }
}
