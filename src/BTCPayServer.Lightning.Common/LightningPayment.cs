using System;

namespace BTCPayServer.Lightning
{
    public class LightningPayment
    {
        public string Id { get; set; }
        public string PaymentHash { get; set; }
        public string Preimage { get; set; }
        public LightningPaymentStatus Status { get; set; }
        public string BOLT11 { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public LightMoney Amount { get; set; }
        public LightMoney AmountSent { get; set; }
        public LightMoney Fee { get; set; }
    }
}
