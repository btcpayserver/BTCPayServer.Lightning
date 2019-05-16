using Newtonsoft.Json;
using System;

namespace BTCPayServer.Lightning.Eclair.Models
{
    public partial class PaymentReceivedEvent
    {
        public string Type { get; set; }
        public long Amount { get; set; }
        public string PaymentHash { get; set; }
        public string FromChannelId { get; set; }
        public long Timestamp { get; set; }
    }
}