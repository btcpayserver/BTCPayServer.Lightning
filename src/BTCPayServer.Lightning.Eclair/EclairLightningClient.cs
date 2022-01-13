using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Eclair.Models;
using NBitcoin;
using NBitcoin.RPC;

namespace BTCPayServer.Lightning.Eclair
{
    public class EclairLightningClient : ILightningClient
    {
        private readonly Uri _address;
        private readonly string _password;
        private readonly Network _network;
        private readonly EclairClient _eclairClient;

        public EclairLightningClient(Uri address, string password, Network network, HttpClient httpClient = null)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            _address = address;
            _password = password;
            _network = network;
            _eclairClient = new EclairClient(address, password, network, httpClient);
        }


        public async Task<LightningInvoice> GetInvoice(string invoiceId,
            CancellationToken cancellation = default(CancellationToken))
        {
            InvoiceResponse result = null;
            try
			{
                result = await _eclairClient.GetInvoice(invoiceId, cancellation);
            }
            catch (EclairClient.EclairApiException ex) when (ex.Error.Error == "Not found" || ex.Error.Error.Contains("Invalid hexadecimal", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            
            GetReceivedInfoResponse info = null;
            try
            {
                info = await _eclairClient.GetReceivedInfo(invoiceId, null, cancellation);
            }
            catch (EclairClient.EclairApiException)
            {
            }

            var parsed = BOLT11PaymentRequest.Parse(result.Serialized, _network);
            var lnInvoice = new LightningInvoice()
            {
                Id = result.PaymentHash,
                Amount = parsed.MinimumAmount,
                ExpiresAt = parsed.ExpiryDate,
                BOLT11 = result.Serialized
            };
            if (DateTimeOffset.UtcNow >= parsed.ExpiryDate)
            {
                lnInvoice.Status = LightningInvoiceStatus.Expired;
            }
            if (info != null && info.Status.Type == "received")
            {
                lnInvoice.AmountReceived = info.Status.Amount;
                lnInvoice.Status = info.Status.Amount >= parsed.MinimumAmount ? LightningInvoiceStatus.Paid : LightningInvoiceStatus.Unpaid;
                lnInvoice.PaidAt = info.Status.ReceivedAt;
            }
            return lnInvoice;
        }

        async Task<LightningInvoice> ILightningClient.CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
            CancellationToken cancellation)
        {
            var result = await _eclairClient.CreateInvoice(
                description,
                amount.MilliSatoshi,
                Convert.ToInt32(expiry.TotalSeconds), null, cancellation);

            var parsed = BOLT11PaymentRequest.Parse(result.Serialized, _network);
            var invoice = new LightningInvoice()
            {
                BOLT11 = result.Serialized,
                Amount = amount,
                Id = result.PaymentHash,
                Status = LightningInvoiceStatus.Unpaid,
                ExpiresAt = parsed.ExpiryDate
            };
            return invoice;
        }
        Task<LightningInvoice> ILightningClient.CreateInvoice(CreateInvoiceParams req, CancellationToken cancellation)
        {
            if (req.DescriptionHash != null)
            {
                throw new NotSupportedException();
            }
            return (this as ILightningClient).CreateInvoice(req.Amount, req.Description, req.Expiry, cancellation);
        }

        public async Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default(CancellationToken))
        {
            return new EclairSession(
               await WebsocketHelper.CreateClientWebSocket(_address.AbsoluteUri,
                  new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.Default.GetBytes($":{_password}"))).ToString(), cancellation), this);
        }

        public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default(CancellationToken))
        {
            var info = await _eclairClient.GetInfo(cancellation);
            var nodeInfo = new LightningNodeInformation()
            {
                BlockHeight = info.BlockHeight
            };
            if (info.PublicAddresses != null)
            {
                nodeInfo.NodeInfoList.AddRange(info.PublicAddresses.Select(s =>
                {
                    var split = s.Split(':');
                    return new NodeInfo(new PubKey(info.NodeId), split[0], int.Parse(split[1]));
                }));
            }
            return nodeInfo;
        }

        public async Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default(CancellationToken))
        {
            try
            {
                var uuid = await _eclairClient.PayInvoice(bolt11, null, null, cancellation);
                while (!cancellation.IsCancellationRequested)
                {
                    var status = await _eclairClient.GetSentInfo(null, uuid, cancellation);
                    if (!status.Any())
                    {
                        continue;
                    }

                    switch (status.First().Status.type)
                    {
                        case "sent":
                            return new PayResponse(PayResult.Ok);
                        case "failed":
                            return new PayResponse(PayResult.CouldNotFindRoute);
                        case "pending":
                            await Task.Delay(200, cancellation);
                            break;
                    }
                }
            }
            catch (EclairClient.EclairApiException)
            {
            }

            return new PayResponse(PayResult.CouldNotFindRoute);
        }

        public async Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest,
            CancellationToken cancellation = default(CancellationToken))
        {
            try
            {
                var result = await _eclairClient.Open(openChannelRequest.NodeInfo.NodeId,
                    openChannelRequest.ChannelAmount.Satoshi
                    , null,
                    Convert.ToInt64(openChannelRequest.FeeRate.SatoshiPerByte), null, cancellation);

                if (result.Contains("created channel", StringComparison.OrdinalIgnoreCase))
                {
                    var channelId = result.Replace("created channel", "").Trim();
                    var channel = await _eclairClient.Channel(channelId, cancellation);
                    switch (channel.State)
                    {
                        case "WAIT_FOR_OPEN_CHANNEL":
                        case "WAIT_FOR_ACCEPT_CHANNEL":
                        case "WAIT_FOR_FUNDING_CREATED":
                        case "WAIT_FOR_FUNDING_SIGNED":
                        case "WAIT_FOR_FUNDING_LOCKED":
                        case "WAIT_FOR_FUNDING_CONFIRMED":
                            return new OpenChannelResponse(OpenChannelResult.NeedMoreConf);
                    }
                }

                if (result.Contains("couldn't publish funding tx", StringComparison.OrdinalIgnoreCase))
                {
                    return new OpenChannelResponse(OpenChannelResult.CannotAffordFunding);
                }

                return new OpenChannelResponse(OpenChannelResult.Ok);
            }
            catch (Exception e) when (e.Message.Contains("not connected", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("no connection to peer", StringComparison.OrdinalIgnoreCase))
            {
                return new OpenChannelResponse(OpenChannelResult.PeerNotConnected);
            }
            catch (Exception e) when (e.Message.Contains("insufficient funds", StringComparison.OrdinalIgnoreCase))
            {
                return new OpenChannelResponse(OpenChannelResult.CannotAffordFunding);
            }
        }

        public Task<BitcoinAddress> GetDepositAddress()
        {
            return _eclairClient.GetNewAddress();
        }

        public async Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo)
        {
            try
            {
                var result = await _eclairClient.Connect(nodeInfo.NodeId, nodeInfo.Host, nodeInfo.Port);
                if (result.StartsWith("already connected", StringComparison.OrdinalIgnoreCase) ||
                    result.StartsWith("connected", StringComparison.OrdinalIgnoreCase))
                    return ConnectionResult.Ok;
                return ConnectionResult.CouldNotConnect;
            }
            catch (Eclair.EclairClient.EclairApiException)
            {
                return ConnectionResult.CouldNotConnect;
            }
        }

        public Task CancelInvoice(string invoiceId)
        {
            throw new NotSupportedException();
        }

        public async Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default(CancellationToken))
        {
            var channels = await _eclairClient.Channels(null, cancellation);
            return channels.Select(response =>
            {
                OutPoint.TryParse(response.Data.Commitments.CommitInput.OutPoint.Replace(":", "-"),
                    out var outPoint);

                return new LightningChannel()
                {
                    IsPublic = ((ChannelFlags)response.Data.Commitments.ChannelFlags) == ChannelFlags.Public,
                    RemoteNode = new PubKey(response.NodeId),
                    IsActive = response.State == "NORMAL",
                    LocalBalance = new LightMoney(response.Data.Commitments.LocalCommit.Spec.ToLocalMsat),
                    Capacity = new LightMoney(response.Data.Commitments.CommitInput.AmountSatoshis),
                    ChannelPoint = outPoint,
                };
            }).ToArray();
        }
    }
}