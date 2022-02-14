using System;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Lightning
{
    public interface ILightningClient
    {
        Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default);
        Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation = default);
        Task<LightningInvoice> CreateInvoice(CreateInvoiceParams createInvoiceRequest, CancellationToken cancellation = default);
        Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default);
        Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default);
        Task<PayResponse> Pay(string bolt11, float? maxFeePercent, CancellationToken cancellation = default);
        Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default);
        Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation = default);
        Task<BitcoinAddress> GetDepositAddress();
        Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo);
        Task CancelInvoice(string invoiceId);
        Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default);
    }

    public interface ILightningInvoiceListener : IDisposable
    {
        Task<LightningInvoice> WaitInvoice(CancellationToken cancellation);
    }
}
