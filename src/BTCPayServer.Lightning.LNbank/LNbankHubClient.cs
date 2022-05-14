using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.LNbank.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace BTCPayServer.Lightning.LNbank
{
    public class LNbankHubClient : ILightningInvoiceListener
    {
        private readonly LNbankLightningClient _lightningClient;
        private readonly HubConnection _connection;
        private readonly CancellationToken _cancellationToken;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public LNbankHubClient(Uri baseUri, string apiToken, LNbankLightningClient lightningClient, CancellationToken cancellation)
        {
            _lightningClient = lightningClient;
            _cancellationToken = cancellation;
            _connection = new HubConnectionBuilder()
                .WithUrl($"{baseUri.AbsoluteUri}plugins/lnbank/hubs/transaction", options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(apiToken);
                })
                .WithAutomaticReconnect()
                .Build();
        }

        public async Task Start(CancellationToken cancellation)
        {
            await _connection.StartAsync(cancellation);
        }

        public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
        {
            try
            {
                LightningInvoice invoice;

                var tcs = new TaskCompletionSource<LightningInvoice>(cancellation);

                _connection.On<TransactionUpdateEvent>("transaction-update", async data =>
                {
                    invoice = await _lightningClient.GetInvoice(data.InvoiceId, cancellation);

                    if (invoice != null)
                        tcs.SetResult(invoice);
                });

                return await tcs.Task;
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                throw new OperationCanceledException(_cts.Token);
            }
        }

        public async void Dispose()
        {
            await DisposeAsync();
        }

        private async Task DisposeAsync()
        {
            await _connection.StopAsync(_cancellationToken);
            await _connection.DisposeAsync();
            _cts.Cancel();
        }
    }
}
