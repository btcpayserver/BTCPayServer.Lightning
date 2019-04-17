using System.Collections.Generic;

namespace BTCPayServer.Lightning.Eclair.Models
{
    public partial class PaymentFailedEvent
    {
        public string Type { get; set; }
        public string PaymentHash { get; set; }
        public List<object> Failures { get; set; }
    }
}