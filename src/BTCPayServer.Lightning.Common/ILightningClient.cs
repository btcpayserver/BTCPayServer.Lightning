using System;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Lightning
{
    public interface ILightningClient
    {
        Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default);
        Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = default);
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
}
