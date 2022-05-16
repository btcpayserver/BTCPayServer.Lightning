using System;
using BTCPayServer.Lightning.LNDhub.Models;
using NBitcoin;

namespace BTCPayServer.Lightning.LndHub
{
    internal class LndHubUtil
    {
        internal static LightningInvoice ToLightningInvoice(InvoiceData data)
        {
            var now = DateTimeOffset.UtcNow;
            var expiresAt = data.CreatedAt + data.ExpireTime;
            var status = expiresAt <= now
                ? LightningInvoiceStatus.Expired
                : data.IsPaid
                    ? LightningInvoiceStatus.Paid
                    : LightningInvoiceStatus.Unpaid;

            var invoice = new LightningInvoice
            {
                Id = data.Id,
                BOLT11 = data.PaymentRequest,
                Status = status,
                ExpiresAt = expiresAt.GetValueOrDefault(),
                Amount = data.Amount,
                AmountReceived = data.IsPaid ? data.Amount : null
            };

            if (data.IsPaid)
                invoice.PaidAt = now;

            return invoice;
        }

        internal static LightningPayment ToLightningPayment(TransactionData data)
        {
            var payment = new LightningPayment
            {
                Id = data.PaymentHash,
                PaymentHash = data.PaymentHash,
                Preimage = data.PaymentPreimage,
                Status = LightningPaymentStatus.Complete,
                CreatedAt = Utils.UnixTimeToDateTime(long.Parse(data.Timestamp)),
                Amount = data.Value - data.Fee,
                AmountSent = data.Value,
                Fee = data.Fee
            };

            return payment;
        }
    }
}
