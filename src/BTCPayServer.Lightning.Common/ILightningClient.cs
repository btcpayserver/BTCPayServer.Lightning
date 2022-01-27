using System;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Lightning
{
    public interface ILightningClient
    {
        Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default(CancellationToken));
        Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation = default(CancellationToken));
        Task<LightningInvoice> CreateInvoice(CreateInvoiceParams createInvoiceRequest, CancellationToken cancellation = default(CancellationToken));
        Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default(CancellationToken));
        Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default(CancellationToken));
        Task<PayResponse> Pay(string bolt11, LightMoney explicitAmount = null, CancellationToken cancellation = default(CancellationToken));
        Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation = default(CancellationToken));
        Task<BitcoinAddress> GetDepositAddress();
        Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo);
        Task CancelInvoice(string invoiceId);
        Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default(CancellationToken));

        Task<bool> VerifyMessage(string message, string signature);
        Task<bool> SignMessage(string message, KeyPath signature);
    }

    public interface ILightningInvoiceListener : IDisposable
    {
        Task<LightningInvoice> WaitInvoice(CancellationToken cancellation);
    }
}
