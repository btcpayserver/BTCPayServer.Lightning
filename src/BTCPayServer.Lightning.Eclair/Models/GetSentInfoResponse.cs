using System;

namespace BTCPayServer.Lightning.Eclair.Models
{
    public partial class GetSentInfoResponse
    {
        public Guid Id { get; set; }
        public string PaymentHash { get; set; }
        public string Preimage { get; set; }
        public long AmountMsat { get; set; }
        public long CreatedAt { get; set; }
        public long CompletedAt { get; set; }
        public PaymentStatus Status { get; set; }
    }
}