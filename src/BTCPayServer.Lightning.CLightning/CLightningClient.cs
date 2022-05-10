using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mono.Unix;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.RPC;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.CLightning
{
	public enum CLightningErrorCode : int
	{
		/* Errors from `pay`, `sendpay`, or `waitsendpay` commands */
		IN_PROGRESS = 200,
		RHASH_ALREADY_USED = 201,
		UNPARSEABLE_ONION = 202,
		DESTINATION_PERM_FAIL = 203,
		TRY_OTHER_ROUTE = 204,
		ROUTE_NOT_FOUND = 205,
		ROUTE_TOO_EXPENSIVE = 206,
		INVOICE_EXPIRED = 207,
		NO_SUCH_PAYMENT = 208,
		UNSPECIFIED_ERROR = 209,
		STOPPED_RETRYING = 210,

		/* `fundchannel` or `withdraw` errors */
		MAX_EXCEEDED = 300,
		CANNOT_AFFORD = 301,
		OUTPUT_IS_DUST = 302,
		BROADCAST_FAIL = 303,
		STILL_SYNCING_BITCOIN = 304,

		/* `connect` errors */
		CONNECT_NO_KNOWN_ADDRESS = 400,
		CONNECT_ALL_ADDRESSES_FAILED = 401,

		/* Errors from `invoice` command */
		LABEL_ALREADY_EXISTS = 900,
		PREIMAGE_ALREADY_EXISTS = 901,

		/*delinvoice errors */
		DATABASE_ERROR = -1,
		LABEL_DOES_NOT_EXIST = 905,
		STATUS_NOT_MATCHED = 906
	}
	public class LightningRPCException : Exception
	{
		public LightningRPCException(string message, int code) : this(message, (CLightningErrorCode)code)
		{
		}
		public LightningRPCException(string message, CLightningErrorCode code) : base(message)
		{
			Code = code;
		}

		public CLightningErrorCode Code
		{
			get;
		}

		[Obsolete("Use Code instead")]
		public int ErrorCode => (int)Code;
	}
	public class CLightningClient : ILightningClient
	{
		public Network Network
		{
			get; private set;
		}
		public Uri Address
		{
			get; private set;
		}

		public CLightningClient(Uri address, Network network)
		{
			if (address == null)
				throw new ArgumentNullException(nameof(address));
			if (network == null)
				throw new ArgumentNullException(nameof(network));
			if (address.Scheme == "file")
			{
				address = new UriBuilder(address) { Scheme = "unix" }.Uri;
			}
			Address = address;
			Network = network;
		}

		public Task<GetInfoResponse> GetInfoAsync(CancellationToken cancellation = default(CancellationToken))
		{
			return SendCommandAsync<GetInfoResponse>("getinfo", cancellation: cancellation);
		}

		public async Task<PeerInfo[]> ListPeersAsync(CancellationToken cancellation = default(CancellationToken))
		{
			var peers = await SendCommandAsync<PeerInfo[]>("listpeers", isArray: true, cancellation: cancellation);
			foreach (var peer in peers)
			{
				peer.Channels = peer.Channels ?? Array.Empty<ChannelInfo>();
			}
			return peers;
		}

		public Task FundChannelAsync(OpenChannelRequest openChannelRequest, CancellationToken cancellation)
		{
			OpenChannelRequest.AssertIsSane(openChannelRequest);
			List<object> parameters = new List<object>();
			parameters.Add(openChannelRequest.NodeInfo.NodeId.ToString());
			parameters.Add(openChannelRequest.ChannelAmount.Satoshi);
			if (openChannelRequest.FeeRate != null)
				parameters.Add($"{openChannelRequest.FeeRate.FeePerK.Satoshi * 4}perkw");
			return SendCommandAsync<object>("fundchannel", parameters.ToArray(), true, cancellation: cancellation);
		}

		public Task ConnectAsync(NodeInfo nodeInfo)
		{
			return SendCommandAsync<object>("connect", new[] { $"{nodeInfo.NodeId}@{nodeInfo.Host}:{nodeInfo.Port}" }, true);
		}

		static Encoding UTF8 = new UTF8Encoding(false);
		internal async Task<T> SendCommandAsync<T>(string command, object[] parameters = null, bool noReturn = false, bool isArray = false, CancellationToken cancellation = default(CancellationToken))
		{
			parameters = parameters ?? Array.Empty<string>();
			using (Socket socket = await Connect())
			{
				using (var networkStream = new NetworkStream(socket))
				{
					using (var textWriter = new StreamWriter(networkStream, UTF8, 1024 * 10, true))
					{
						using (var jsonWriter = new JsonTextWriter(textWriter))
						{
							var req = new JObject();
							req.Add("id", 0);
							req.Add("method", command);
							req.Add("params", new JArray(parameters));
							await req.WriteToAsync(jsonWriter, cancellation);
							await jsonWriter.FlushAsync(cancellation);
						}
						await textWriter.FlushAsync();
					}
					await networkStream.FlushAsync(cancellation);
					using (var textReader = new StreamReader(networkStream, UTF8, false, 1024 * 10, true))
					{
						using (var jsonReader = new JsonTextReader(textReader))
						{
							var resultAsync = JObject.LoadAsync(jsonReader, cancellation);

							// without this hack resultAsync is blocking even if cancellation happen
							using (cancellation.Register(() =>
							 {
								 socket.Dispose();
							 }))
							{
                                try
                                {
                                    var result = await resultAsync;
                                    var error = result.Property("error");
                                    if (error != null)
                                    {
                                        throw new LightningRPCException(error.Value["message"].Value<string>(), error.Value["code"].Value<int>());
                                    }
                                    if (noReturn)
                                        return default;
                                    if (isArray)
                                    {
                                        return result["result"].Children().First().Children().First().ToObject<T>();
                                    }
                                    return result["result"].ToObject<T>();
                                }
                                catch when (cancellation.IsCancellationRequested)
                                {
                                    cancellation.ThrowIfCancellationRequested();
                                    throw new NotSupportedException(); // impossible
                                }
                            }
						}
					}
				}
			}
		}

		private async Task<Socket> Connect()
		{
			Socket socket = null;
			EndPoint endpoint = null;
			if (Address.Scheme == "tcp" || Address.Scheme == "tcp")
			{
				var domain = Address.DnsSafeHost;
				if (!IPAddress.TryParse(domain, out IPAddress address))
				{
					address = (await Dns.GetHostAddressesAsync(domain)).FirstOrDefault();
					if (address == null)
						throw new Exception("Host not found");
				}
				socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				endpoint = new IPEndPoint(address, Address.Port);
			}
			else if (Address.Scheme == "unix")
			{
				var path = Address.AbsoluteUri.Remove(0, "unix:".Length);
				if (!path.StartsWith("/"))
					path = "/" + path;
				while (path.Length >= 2 && (path[0] != '/' || path[1] == '/'))
				{
					path = path.Remove(0, 1);
				}
				if (path.Length < 2)
					throw new FormatException("Invalid unix url");
				socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
				endpoint = new UnixEndPoint(path);
			}
			else
				throw new NotSupportedException($"Protocol {Address.Scheme} for clightning not supported");

			await socket.ConnectAsync(endpoint);
			return socket;
		}

		public async Task<BitcoinAddress> NewAddressAsync()
		{
			var obj = await SendCommandAsync<JObject>("newaddr");
			var addr = obj.ContainsKey("address") ? "address": "bech32";
			return BitcoinAddress.Create(obj.Property(addr).Value.Value<string>(), Network);
		}

		public async Task<CLightningChannel[]> ListChannelsAsync(ShortChannelId ShortChannelId = null, CancellationToken cancellation = default(CancellationToken))
		{
			var resp =
				ShortChannelId == null
				? await SendCommandAsync<CLightningChannel[]>("listchannels", null, false, true, cancellation)
				: await SendCommandAsync<CLightningChannel[]>("listchannels", new[] { ShortChannelId.ToString() }, false, true, cancellation);

			return resp;
		}

        async Task<LightningPayment> ILightningClient.GetPayment(string paymentHash, CancellationToken cancellation)
        {
            var payments = await SendCommandAsync<CLightningPayment[]>("listpays", new[] { null, paymentHash }, false, true, cancellation);
            if (payments.Length == 0)
                return null;
            return ToLightningPayment(payments[0]);
        }

		async Task<LightningInvoice> ILightningClient.GetInvoice(string invoiceId, CancellationToken cancellation)
		{
			var invoices = await SendCommandAsync<CLightningInvoice[]>("listinvoices", new[] { invoiceId }, false, true, cancellation);
			if (invoices.Length == 0)
				return null;
			return ToLightningInvoice(invoices[0]);
		}

        private async Task<PayResponse> PayAsync(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation)
        {
            try
            {
                if (bolt11 == null)
                    throw new ArgumentNullException(nameof(bolt11));

                bolt11 = bolt11.Replace("lightning:", "").Replace("LIGHTNING:", "");
                var explicitAmount = payParams?.Amount;
				var feePercent = payParams?.MaxFeePercent;
				if (feePercent is null && payParams?.MaxFeeFlat is Money m)
				{
					var pr = BOLT11PaymentRequest.Parse(bolt11, Network);
					var amountSat = (explicitAmount ?? pr.MinimumAmount).ToUnit(LightMoneyUnit.Satoshi);
					feePercent = (double)(m.Satoshi / amountSat) * 100;
				}
				var response = await SendCommandAsync<CLightningPayResponse>("pay", new object[] { bolt11, explicitAmount?.MilliSatoshi, null, null, feePercent }, false, cancellation: cancellation);

                return new PayResponse(PayResult.Ok, new PayDetails
                {
                    TotalAmount = response.AmountSent,
                    FeeAmount = response.AmountSent - response.Amount
                });
            }
            catch (LightningRPCException ex)
            {
                var result =
                    ex.Code == CLightningErrorCode.ROUTE_NOT_FOUND || ex.Code == CLightningErrorCode.STOPPED_RETRYING
                        ? PayResult.CouldNotFindRoute
                        : PayResult.Error;
                return new PayResponse(result, ex.Message);
            }
        }

        async Task<PayResponse> ILightningClient.Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation)
        {
            return await PayAsync(bolt11, payParams, cancellation);
        }

        async Task<PayResponse> ILightningClient.Pay(string bolt11, CancellationToken cancellation)
        {
            return await PayAsync(bolt11, null, cancellation);
        }

		static NBitcoin.DataEncoders.DataEncoder InvoiceIdEncoder = NBitcoin.DataEncoders.Encoders.Base58;

		async Task<LightningInvoice> CreateInvoice(LightMoney amount, uint256 descriptionHash, TimeSpan expiry, CancellationToken cancellation)
		{
			var id = InvoiceIdEncoder.EncodeData(RandomUtils.GetBytes(20));
			var invoice = await SendCommandAsync<CLightningInvoice>("invoicewithdescriptionhash", new object[] { amount.MilliSatoshi, id, descriptionHash.ToString(), Math.Max(0, (int)expiry.TotalMilliseconds) }, cancellation: cancellation);
			invoice.Label = id;
			invoice.MilliSatoshi = amount;
			invoice.Status = "unpaid";
			return ToLightningInvoice(invoice);
		}

		async Task<LightningInvoice> ILightningClient.CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation)
		{
			var id = InvoiceIdEncoder.EncodeData(RandomUtils.GetBytes(20));
            var msat = amount == LightMoney.Zero ? "any" : amount.MilliSatoshi.ToString();
			var invoice = await SendCommandAsync<CLightningInvoice>("invoice", new object[] { msat, id, description ?? "", Math.Max(0, (int)expiry.TotalSeconds) }, cancellation: cancellation);
			invoice.Label = id;
			invoice.MilliSatoshi = amount;
			invoice.Status = "unpaid";
			return ToLightningInvoice(invoice);
		}
		Task<LightningInvoice> ILightningClient.CreateInvoice(CreateInvoiceParams req, CancellationToken cancellation)
		{
			return req.DescriptionHash != null ? CreateInvoice(req.Amount, req.DescriptionHash, req.Expiry, cancellation) : (this as ILightningClient).CreateInvoice(req.Amount, req.Description, req.Expiry, cancellation);
		}

		async Task<ConnectionResult> ILightningClient.ConnectTo(NodeInfo nodeInfo)
		{
			try
			{
				await ConnectAsync(nodeInfo);
			}
			catch (LightningRPCException ex) when (ex.Code == CLightningErrorCode.CONNECT_ALL_ADDRESSES_FAILED || ex.Code == CLightningErrorCode.CONNECT_NO_KNOWN_ADDRESS)
			{
				return ConnectionResult.CouldNotConnect;
			}
			return ConnectionResult.Ok;
		}

		public async Task CancelInvoice(string invoiceId)
		{
			await SendCommandAsync<CLightningInvoice>("delinvoice", new object[] { invoiceId, "unpaid" }, cancellation: CancellationToken.None);
		}

		async Task<LightningChannel[]> ILightningClient.ListChannels(CancellationToken cancellation)
		{
			var listPeersAsync = this.ListPeersAsync(cancellation);
			List<LightningChannel> channels = new List<LightningChannel>();
			foreach (var peer in await listPeersAsync)
			{
				foreach (var channel in peer.Channels)
				{
					channels.Add(new LightningChannel()
					{
						RemoteNode = new PubKey(peer.Id),
						IsPublic = !channel.Private,
						LocalBalance = channel.ToUs,
						ChannelPoint = new OutPoint(channel.FundingTxId, channel.ShortChannelId.TxOutIndex),
						Capacity = channel.Total,
						IsActive = channel.State == "CHANNELD_NORMAL"
					});
				}
			}
			return channels.ToArray();
		}

		internal static LightningInvoice ToLightningInvoice(CLightningInvoice invoice) =>
            new LightningInvoice
			{
				Id = invoice.Label,
				Amount = invoice.MilliSatoshi,
				AmountReceived = invoice.MilliSatoshiReceived,
				BOLT11 = invoice.BOLT11,
				Status = ToInvoiceStatus(invoice.Status),
				PaidAt = invoice.PaidAt,
				ExpiresAt = invoice.ExpiryAt
			};

        internal static LightningPayment ToLightningPayment(CLightningPayment payment) =>
            new LightningPayment
            {
                Id = payment.Label,
                Amount = payment.MilliSatoshi,
                AmountSent = payment.MilliSatoshiSent,
                Fee = payment.MilliSatoshiSent != null && payment.MilliSatoshi != null ? payment.MilliSatoshiSent - payment.MilliSatoshi : null,
                BOLT11 = payment.BOLT11,
                Status = ToPaymentStatus(payment.Status),
                CreatedAt = payment.CreatedAt,
                PaymentHash = payment.PaymentHash.ToString(),
                Preimage = payment.Preimage.ToString()
            };

		public static LightningInvoiceStatus ToInvoiceStatus(string status)
		{
			switch (status)
			{
				case "paid":
					return LightningInvoiceStatus.Paid;
				case "unpaid":
					return LightningInvoiceStatus.Unpaid;
				case "expired":
					return LightningInvoiceStatus.Expired;
				default:
					throw new NotSupportedException($"'{status}' can't map to any LightningInvoiceStatus");
			}
		}

        public static LightningPaymentStatus ToPaymentStatus(string status)
        {
            switch (status)
            {
                case "pending":
                    return LightningPaymentStatus.Pending;
                case "failed":
                    return LightningPaymentStatus.Failed;
                case "complete":
                    return LightningPaymentStatus.Complete;
                default:
                    throw new NotSupportedException($"'{status}' can't map to any LightningPaymentStatus");
            }
        }

		Task<ILightningInvoiceListener> ILightningClient.Listen(CancellationToken cancellation)
		{
			return Task.FromResult<ILightningInvoiceListener>(new CLightningInvoiceListener(this));
		}

		async Task<LightningNodeInformation> ILightningClient.GetInfo(CancellationToken cancellation)
		{
			var info = await GetInfoAsync(cancellation);
			return ToLightningNodeInformation(info);
		}

		async Task<OpenChannelResponse> ILightningClient.OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation)
		{
			retry:
			try
			{
				await FundChannelAsync(openChannelRequest, cancellation);
			}
			catch (LightningRPCException ex) when (ex.Code == CLightningErrorCode.STILL_SYNCING_BITCOIN)
			{
				await Task.Delay(1000, cancellation);
				goto retry;
			}
			catch (LightningRPCException ex) when (ex.Code == CLightningErrorCode.CANNOT_AFFORD)
			{
				return new OpenChannelResponse(OpenChannelResult.CannotAffordFunding);
			}
			catch (LightningRPCException ex) when (ex.Code == CLightningErrorCode.CONNECT_ALL_ADDRESSES_FAILED ||
												   ex.Code == CLightningErrorCode.CONNECT_NO_KNOWN_ADDRESS ||
												   ex.Message == "Peer not connected" ||
												   ex.Message == "Unknown peer" ||
												   ex.Message == "Unable to connect, no address known for peer")
			{
				return new OpenChannelResponse(OpenChannelResult.PeerNotConnected);
			}
			catch (LightningRPCException ex) when (ex.Message.Contains("CHANNELD_AWAITING_LOCKIN"))
			{
				return new OpenChannelResponse(OpenChannelResult.NeedMoreConf);
			}
			catch (LightningRPCException ex) when (
				ex.Message.Contains("CHANNELD_NORMAL") ||
				ex.Message.Contains("CHANNELD_SHUTTING_DOWN") ||
				ex.Message.Contains("CLOSINGD_SIGEXCHANGE") ||
				ex.Message.Contains("CLOSINGD_COMPLETE") ||
				ex.Message.Contains("AWAITING_UNILATERAL") ||
				ex.Message.Contains("FUNDING_SPEND_SEEN") ||
				ex.Message.Contains("ONCHAIN"))
			{
				return new OpenChannelResponse(OpenChannelResult.AlreadyExists);
			}
			return new OpenChannelResponse(OpenChannelResult.Ok);
		}

		async Task<BitcoinAddress> ILightningClient.GetDepositAddress()
		{
			return await this.NewAddressAsync();
		}

		public static LightningNodeInformation ToLightningNodeInformation(GetInfoResponse info)
		{
			var pubkey = new PubKey(info.Id);
			var nodeInfo = new LightningNodeInformation()
			{
				BlockHeight = info.BlockHeight
			};
			if (info.Address != null)
			{
				nodeInfo.NodeInfoList.AddRange(info.Address.Select(addr => new NodeInfo(pubkey, addr.Address, addr.Port == 0 ? 9735 : addr.Port)));
			}
			return nodeInfo;
		}
	}

	class CLightningInvoiceListener : ILightningInvoiceListener
	{
		CancellationTokenSource _Cts = new CancellationTokenSource();
		CLightningClient _Parent;
		public CLightningInvoiceListener(CLightningClient parent)
		{
			if (parent == null)
				throw new ArgumentNullException(nameof(parent));
			_Parent = parent;
		}
		public void Dispose()
		{
			_Cts.Cancel();
		}

		long lastInvoiceIndex = 99999999999;

		public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
		{
			using (var cancellation2 = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _Cts.Token))
			{
				var invoice = await _Parent.SendCommandAsync<CLightningInvoice>("waitanyinvoice", new object[] { lastInvoiceIndex }, cancellation: cancellation2.Token);
				lastInvoiceIndex = invoice.PayIndex.Value;
				return CLightningClient.ToLightningInvoice(invoice);
			}
		}
	}
}
