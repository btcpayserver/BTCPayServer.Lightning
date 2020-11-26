using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.LNbank.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.LNbank
{
    public class LNbankHubClient: ILightningInvoiceListener
    {
        private readonly LNbankLightningClient _lightningClient;
        private readonly HubConnection _connection;
        private readonly CancellationToken _cancellationToken;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private LightningInvoice _invoice;

        public LNbankHubClient(Uri baseUri, string apiToken, LNbankLightningClient lightningClient, CancellationToken cancellation)
        {
            _lightningClient = lightningClient;
            _cancellationToken = cancellation;
            _connection = new HubConnectionBuilder()
                .WithUrl($"{baseUri.AbsoluteUri}Hubs/Transaction", options => {
                    options.AccessTokenProvider = () => Task.FromResult(apiToken);
                })
                .WithAutomaticReconnect()
                .Build();
        }

        public async Task SetupAsync(CancellationToken cancellation)
        {
            _connection.On<TransactionUpdateEvent>("transaction-update", async data =>
            {
                _invoice = await _lightningClient.GetInvoice(data.InvoiceId, cancellation);
            });

            await _connection.StartAsync(_cancellationToken);
        }

        public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
        {
            try
            {
                while (_invoice == null && !cancellation.IsCancellationRequested) {}
                return _invoice;
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
