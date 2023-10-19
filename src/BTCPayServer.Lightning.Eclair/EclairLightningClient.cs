using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Eclair.Models;
using NBitcoin;

namespace BTCPayServer.Lightning.Eclair
{
    public class EclairLightningClient : ILightningClient
    {
        private readonly Uri _address;
        private readonly string _username;
        private readonly string _password;
        private readonly Network _network;
        private readonly EclairClient _eclairClient;

        public EclairLightningClient(Uri address, string password, Network network, HttpClient httpClient = null) :
            this(address, null, password, network, httpClient)
        {
        }

        public EclairLightningClient(Uri address, string username, string password, Network network, HttpClient httpClient = null)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            _address = address;
            _username = username;
            _password = password;
            _network = network;
            _eclairClient = new EclairClient(address, username, password, network, httpClient);
        }
        
        public async Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            try
            {
                var result = await _eclairClient.GetInvoice(invoiceId, cancellation);
                return await ToLightningInvoice(result, cancellation);
            }
            catch (EclairClient.EclairApiException ex) when (ex.Error.Error == "Not found" || ex.Error.Error.Contains("Invalid hexadecimal", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }
        
        public async Task<LightningInvoice> GetInvoice(uint256 paymentHash, CancellationToken cancellation = default) =>
            await GetInvoice(paymentHash.ToString(), cancellation);

        public async Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = default) =>
            await ListInvoices(null, cancellation);

        public async Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation = default)
        {
            int? from = request?.OffsetIndex != null ? (int)request.OffsetIndex.Value : null;
            var invoices = request is { PendingOnly: true }
                ? await _eclairClient.ListPendingInvoices(from, null, cancellation)
                : await _eclairClient.ListInvoices(from, null, cancellation);

            return await Task.WhenAll(invoices.Select(invoice => ToLightningInvoice(invoice, cancellation)));
        }

        private async Task<LightningInvoice> ToLightningInvoice(InvoiceResponse invoice, CancellationToken cancellation)
        {
            var parsed = BOLT11PaymentRequest.Parse(invoice.Serialized, _network);
            var invoiceId = invoice.PaymentHash;
            var lnInvoice = new LightningInvoice
            {
                Id = invoiceId,
                PaymentHash = invoice.PaymentHash,
                Amount = parsed.MinimumAmount,
                ExpiresAt = parsed.ExpiryDate,
                BOLT11 = invoice.Serialized
            };
            if (DateTimeOffset.UtcNow >= parsed.ExpiryDate)
            {
                lnInvoice.Status = LightningInvoiceStatus.Expired;
            }
            
            GetReceivedInfoResponse info = null;
            try
            {
                info = await _eclairClient.GetReceivedInfo(invoiceId, null, cancellation);
            }
            catch (EclairClient.EclairApiException)
            {
            }

            if (info != null && info.Status.Type == "received")
            {
                lnInvoice.AmountReceived = info.Status.Amount;
                lnInvoice.Status = info.Status.Amount >= parsed.MinimumAmount ? LightningInvoiceStatus.Paid : LightningInvoiceStatus.Unpaid;
                lnInvoice.PaidAt = info.Status.ReceivedAt;
                lnInvoice.Preimage = info.PaymentPreimage;
            }

            return lnInvoice;
        }

        public async Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = default)
        {
            var result = await _eclairClient.GetSentInfo(paymentHash, null, cancellation);

            var sentInfo = result.First();
            var fees = sentInfo.Status.FeesPaid;
            var payment = new LightningPayment
            {
                Id = sentInfo.Id.ToString(),
                Preimage = sentInfo.Status.PaymentPreimage,
                PaymentHash = sentInfo.PaymentHash,
                CreatedAt = sentInfo.CreatedAt,
                Amount = sentInfo.Amount,
                AmountSent = sentInfo.Amount + fees,
                Fee = fees
            };

            switch (sentInfo.Status.Type)
            {
                case "pending":
                    payment.Status = LightningPaymentStatus.Pending;
                    break;
                case "failed":
                    payment.Status = LightningPaymentStatus.Failed;
                    break;
                case "sent":
                    payment.Status = LightningPaymentStatus.Complete;
                    break;
                default:
                    payment.Status = LightningPaymentStatus.Unknown;
                    break;
            }

            return payment;
        }

        public Task<LightningPayment[]> ListPayments(CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }

        public Task<LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }

        async Task<LightningInvoice> ILightningClient.CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
            CancellationToken cancellation)
        {
            var result = await _eclairClient.CreateInvoice(
                description,
                amount.MilliSatoshi,
                Convert.ToInt32(expiry.TotalSeconds), null, cancellation);

            var parsed = BOLT11PaymentRequest.Parse(result.Serialized, _network);
            var invoice = new LightningInvoice
            {
                BOLT11 = result.Serialized,
                Amount = amount,
                Id = result.PaymentHash,
                Status = LightningInvoiceStatus.Unpaid,
                ExpiresAt = parsed.ExpiryDate,
                PaymentHash = result.PaymentHash
            };
            return invoice;
        }
        Task<LightningInvoice> ILightningClient.CreateInvoice(CreateInvoiceParams req, CancellationToken cancellation)
        {
            if (req.DescriptionHash is not null)
            {
                throw new NotSupportedException("DescriptionHash isn't supported");
            }
            return (this as ILightningClient).CreateInvoice(req.Amount, req.Description, req.Expiry, cancellation);
        }

        public async Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default)
        {
            return new EclairSession(
               await WebsocketHelper.CreateClientWebSocket(_address.AbsoluteUri,
                  new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.Default.GetBytes($"{_username??string.Empty}:{_password}"))).ToString(), cancellation), this);
        }

        public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default)
        {
            var info = await _eclairClient.GetInfo(cancellation);
            var nodeInfo = new LightningNodeInformation
            {
                BlockHeight = info.BlockHeight,
                Version = info.Version,
                Color = info.Color,
                Alias = info.Alias
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

        public async Task<LightningNodeBalance> GetBalance(CancellationToken cancellation = default)
        {
            var globalBalance = _eclairClient.GlobalBalance(cancellation);
            var usableBalances = _eclairClient.UsableBalances(cancellation);
            await Task.WhenAll(globalBalance, usableBalances);

            var global = globalBalance.Result;
            var usable = usableBalances.Result;
            
            var onchain = new OnchainBalance
            { 
                Confirmed = new Money(global.Onchain.Confirmed, MoneyUnit.BTC),
                Unconfirmed = new Money(global.Onchain.Unconfirmed, MoneyUnit.BTC),
                Reserved = null // Not supported by Eclair
            };
            var offchain = new OffchainBalance
            {
                Opening = 
                    global.Offchain.WaitForFundingConfirmed + 
                    global.Offchain.WaitForChannelReady + 
                    global.Offchain.WaitForPublishFutureCommitment,
                Local = global.Offchain.Normal.ToLocal,
                Remote = usable.Sum(channel => channel.CanReceive),
                Closing = 
                    global.Offchain.Closing.LocalCloseBalance.ToLocal +
                    global.Offchain.Closing.RemoteCloseBalance.ToLocal +
                    global.Offchain.Closing.MutualCloseBalance.ToLocal + 
                    global.Offchain.Closing.UnknownCloseBalance.ToLocal
            };
            
            return new LightningNodeBalance(onchain, offchain);
        }

        public async Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default)
        {
            return await Pay(bolt11, null, cancellation);
        }

        public async Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation = default)
        {
            // Pay the invoice - cancel after timeout, potentially caused by hold invoices
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var timeout = payParams?.SendTimeout ?? PayInvoiceParams.DefaultSendTimeout;
            cts.CancelAfter(timeout);

            try
            {
                var req = new PayInvoiceRequest
                {
                    Invoice = bolt11,
                    AmountMsat = payParams?.Amount?.MilliSatoshi,
                    MaxFeePct = payParams?.MaxFeePercent != null
                        ? (int)Math.Round(payParams.MaxFeePercent.Value)
                        : null,
                    MaxFeeFlatSat = payParams?.MaxFeeFlat?.Satoshi
                };
                var uuid = await _eclairClient.PayInvoice(req, cts.Token);
                while (!cts.Token.IsCancellationRequested)
                {
                    var status = await _eclairClient.GetSentInfo(null, uuid, cts.Token);
                    if (!status.Any())
                    {
                        continue;
                    }

                    var sentInfo = status.First();
                    switch (sentInfo.Status.Type)
                    {
                        case "sent":
                            return new PayResponse(PayResult.Ok,
                                new PayDetails
                                {
                                    TotalAmount = sentInfo.Amount,
                                    FeeAmount = sentInfo.Status.FeesPaid,
                                    PaymentHash = new uint256(sentInfo.PaymentHash),
                                    Preimage = new uint256(sentInfo.Status.PaymentPreimage),
                                    Status = LightningPaymentStatus.Complete
                                });
                        case "failed":
                            var failure = sentInfo.Status.Failures.First();
                            var result = failure.FailureMessage.Contains("route")
                                ? PayResult.CouldNotFindRoute
                                : PayResult.Error;
                            return new PayResponse(result, failure.FailureMessage);
                        case "pending":
                            await Task.Delay(200, cts.Token);
                            break;
                    }
                }
            }
            catch (EclairClient.EclairApiException exception)
            {
                return new PayResponse(PayResult.Error, exception.Message);
            }
            catch (Exception exception)
            {
                return cts.Token.IsCancellationRequested
                    ? new PayResponse(PayResult.Unknown)
                    : new PayResponse(PayResult.Error, exception.Message);
            }
            return new PayResponse(PayResult.Unknown);
        }

        public async Task<PayResponse> Pay(PayInvoiceParams payParams, CancellationToken cancellation = default)
        {
            // Pay the invoice - cancel after timeout, potentially caused by hold invoices
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var timeout = payParams?.SendTimeout ?? PayInvoiceParams.DefaultSendTimeout;
            cts.CancelAfter(timeout);

            try
            {
                var req = new SendToNodeRequest
                {
                    NodeId = payParams.Destination?.ToString(),
                    AmountMsat = payParams.Amount?.MilliSatoshi,
                    MaxFeePct = payParams.MaxFeePercent != null ? (int)Math.Round(payParams.MaxFeePercent.Value) : null,
                    MaxFeeFlatSat = payParams.MaxFeeFlat?.Satoshi,

                };
                var uuid = await _eclairClient.SendToNode(req, cts.Token);
                while (!cts.Token.IsCancellationRequested)
                {
                    var status = await _eclairClient.GetSentInfo(null, uuid, cts.Token);
                    if (!status.Any())
                    {
                        continue;
                    }

                    var sentInfo = status.First();
                    switch (sentInfo.Status.Type)
                    {
                        case "sent":
                            return new PayResponse(PayResult.Ok, new PayDetails
                            {
                                TotalAmount = sentInfo.Amount,
                                FeeAmount = sentInfo.Status.FeesPaid,
                                PaymentHash = new uint256(sentInfo.PaymentHash),
                                Preimage = new uint256(sentInfo.Status.PaymentPreimage),
                                Status = LightningPaymentStatus.Complete
                            });
                        case "failed":
                            var failure = sentInfo.Status.Failures.First();
                            var result = failure.FailureMessage.Contains("route")
                                ? PayResult.CouldNotFindRoute
                                : PayResult.Error;
                            return new PayResponse(result, failure.FailureMessage);
                        case "pending":
                            await Task.Delay(200, cts.Token);
                            break;
                    }
                }
            }
            catch (EclairClient.EclairApiException exception)
            {
                return new PayResponse(PayResult.Error, exception.Message);
            }
            catch (Exception exception)
            {
                return cts.Token.IsCancellationRequested
                    ? new PayResponse(PayResult.Unknown)
                    : new PayResponse(PayResult.Error, exception.Message);
            }
            return new PayResponse(PayResult.Unknown);
        }

        public async Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest,
            CancellationToken cancellation = default)
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
            catch (Exception e) when (e.Message.Contains("not connected", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("no connection to peer", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return new OpenChannelResponse(OpenChannelResult.PeerNotConnected);
            }
            catch (Exception e) when (e.Message.Contains("insufficient funds", StringComparison.OrdinalIgnoreCase))
            {
                return new OpenChannelResponse(OpenChannelResult.CannotAffordFunding);
            }
        }

        public Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = default)
        {
            return _eclairClient.GetNewAddress(cancellation);
        }

        public async Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation = default)
        {
            try
            {
                var result = await _eclairClient.Connect(nodeInfo.NodeId, nodeInfo.Host, nodeInfo.Port, cancellation);
                if (result.StartsWith("already connected", StringComparison.OrdinalIgnoreCase) ||
                    result.StartsWith("connected", StringComparison.OrdinalIgnoreCase))
                    return ConnectionResult.Ok;
                return ConnectionResult.CouldNotConnect;
            }
            catch (EclairClient.EclairApiException)
            {
                return ConnectionResult.CouldNotConnect;
            }
        }

        public Task CancelInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            throw new NotSupportedException();
        }

        public async Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default)
        {
            var channels = await _eclairClient.Channels(null, cancellation);
            return channels.Select(response =>
            {
                var outpointStr = response.Data?.Commitments?.CommitInput?.OutPoint?.Replace(":", "-");
                OutPoint outPoint = null;
                if (outpointStr != null)
                    OutPoint.TryParse(outpointStr, out outPoint);

                return new LightningChannel
                {
                    IsPublic = response.Data.Commitments.IsPublic,
                    RemoteNode = new PubKey(response.NodeId),
                    IsActive = response.State == "NORMAL",
                    LocalBalance = new LightMoney(response.Data.Commitments.LocalCommit.Spec.ToLocalMsat),
                    Capacity = new LightMoney(response.Data.Commitments.CommitInput.AmountSatoshis, LightMoneyUnit.Satoshi),
                    ChannelPoint = outPoint,
                };
            }).ToArray();
        }

        public override string ToString()
        {
            var result= $"type=eclair;server={_address}";
            if (_username is { })
                result += $";username={_username}";
            if (_password is { })
                result += $";password={_password}";
            return result;
            
        }
    }
}
