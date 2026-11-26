using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Lightning
{
    public interface ILightningClient
    {
        Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default);
        Task<LightningInvoice> GetInvoice(uint256 paymentHash, CancellationToken cancellation = default);
        Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = default);
        Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation = default);
        Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = default);
        Task<LightningPayment[]> ListPayments(CancellationToken cancellation = default);
        Task<LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation = default);
        Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation = default);
        Task<LightningInvoice> CreateInvoice(CreateInvoiceParams createInvoiceRequest, CancellationToken cancellation = default);
        Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default);
        Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default);
        Task<LightningNodeBalance> GetBalance(CancellationToken cancellation = default);
        Task<PayResponse> Pay(PayInvoiceParams payParams, CancellationToken cancellation = default);
        Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation = default);
        Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default);
        Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation = default);
        Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = default);
        Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation = default);
        Task CancelInvoice(string invoiceId, CancellationToken cancellation = default);
        Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default);

    }

    public interface ILightningInvoiceListener : IDisposable
    {
        Task<LightningInvoice> WaitInvoice(CancellationToken cancellation);
    }

    public interface ILightningConnectionStringHandler
    {
        ILightningClient Create(string connectionString, Network network, out string error);
        
    }

    public static class LightningConnectionStringHelper
    {
        public static Dictionary<string, string> ExtractValues(string connectionString, out string type)
        {
            var parts = connectionString.Split(new [] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var keyValues = new Dictionary<string, string>();
            foreach (var part in parts.Select(p => p.Trim()))
            {
                var idx = part.IndexOf('=');
                if (idx == -1)
                {
                    throw new FormatException("The format of the connectionString should a list of key=value delimited by semicolon");
                }
                var key = part.Substring(0, idx).Trim().ToLowerInvariant();
                var value = part.Substring(idx + 1).Trim();
                if (!keyValues.TryAdd(key, value))
                {
                    throw new FormatException($"Duplicate key {key}");
                }
            }
            if (!keyValues.TryGetValue("type", out type))
            {
                throw new FormatException("The key 'type' is mandatory");
            }
            return keyValues;
        }
    }
}
