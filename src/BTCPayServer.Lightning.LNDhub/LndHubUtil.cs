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
            var status = ToLightningInvoiceStatus(data);

            var invoice = new LightningInvoice
            {
                Id = data.Id.ToString(),
                BOLT11 = data.PaymentRequest,
                Status = status,
                ExpiresAt = expiresAt.GetValueOrDefault(),
                Amount = data.Amount,
                AmountReceived = data.IsPaid ? data.Amount : null,
                PaymentHash = data.PaymentHash
            };

            if (data.IsPaid)
                invoice.PaidAt = now;

            return invoice;
        }

        internal static LightningInvoiceStatus ToLightningInvoiceStatus(InvoiceData data)
        {
            var expiresAt = data.CreatedAt + data.ExpireTime;
            return expiresAt <= DateTimeOffset.UtcNow
                ? LightningInvoiceStatus.Expired
                : data.IsPaid
                    ? LightningInvoiceStatus.Paid
                    : LightningInvoiceStatus.Unpaid;;
        }

        internal static LightningPayment ToLightningPayment(TransactionData data)
        {
            var paymentHash = data.PaymentHash.ToString();
            var payment = new LightningPayment
            {
                Id = paymentHash,
                PaymentHash = paymentHash,
                Preimage = data.PaymentPreimage,
                Status = ToLightningPaymentStatus(data),
                CreatedAt = data.Timestamp,
                Amount = data.Value - data.Fee,
                AmountSent = data.Value,
                Fee = data.Fee
            };

            return payment;
        }

        internal static LightningPaymentStatus ToLightningPaymentStatus(TransactionData data)
        {
            return data.Value != null && data.Fee != null
                ? LightningPaymentStatus.Complete
                : LightningPaymentStatus.Pending;
        }
    }
}
