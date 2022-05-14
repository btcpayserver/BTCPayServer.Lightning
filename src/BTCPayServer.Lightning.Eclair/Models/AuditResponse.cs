using System.Collections.Generic;

namespace BTCPayServer.Lightning.Eclair.Models
{
    public partial class AuditResponse
    {
        public List<AuditResponseSent> Sent { get; set; }
        public List<AuditResponseReceived> Received { get; set; }
        public List<AuditResponseRelayed> Relayed { get; set; }

        public partial class AuditResponseReceived
        {
            public long Amount { get; set; }
            public string PaymentHash { get; set; }
            public string FromChannelId { get; set; }
            public long Timestamp { get; set; }
        }

        public partial class AuditResponseRelayed
        {
            public long AmountIn { get; set; }
            public long AmountOut { get; set; }
            public string PaymentHash { get; set; }
            public string FromChannelId { get; set; }
            public string ToChannelId { get; set; }
            public long Timestamp { get; set; }
        }

        public partial class AuditResponseSent
        {
            public long Amount { get; set; }
            public long FeesPaid { get; set; }
            public string PaymentHash { get; set; }
            public string PaymentPreimage { get; set; }
            public string ToChannelId { get; set; }
            public long Timestamp { get; set; }
        }
    }
}
