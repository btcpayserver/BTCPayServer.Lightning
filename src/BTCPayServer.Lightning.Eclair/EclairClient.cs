using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.Eclair.Models;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BTCPayServer.Lightning.Eclair
{
	public class EclairClient
	{
		private readonly Uri _address;
		private readonly string _password;
		private readonly HttpClient _httpClient;
		private static readonly HttpClient SharedClient = new HttpClient();

		public Network Network { get; }

		public EclairClient(Uri address, string password, Network network, HttpClient httpClient = null)
		{
			if (address == null)
				throw new ArgumentNullException(nameof(address));
			if (network == null)
				throw new ArgumentNullException(nameof(network));
			_address = address;
			_password = password;
			Network = network;
			_httpClient = httpClient ?? SharedClient;
		}

		public async Task<GetInfoResponse> GetInfo(CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<NoRequestModel, GetInfoResponse>("getinfo", NoRequestModel.Instance, cts);
		}

		public async Task<string> Connect(string uri, CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<ConnectUriRequest, string>("connect", new ConnectUriRequest()
			{
				Uri = uri
			}, cts);
		}

		public async Task<string> Connect(PubKey nodeId, string host, int? port = null,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<ConnectManualRequest, string>("connect", new ConnectManualRequest()
			{
				Host = host,
				Port = port,
				NodeId = nodeId.ToString()
			}, cts);
		}

		public async Task<string> Open(PubKey nodeId, long fundingSatoshis, int? pushMsat = null,
			long? fundingFeerateSatByte = null, ChannelFlags? channelFlags = null,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<OpenRequest, string>("open", new OpenRequest()
			{
				NodeId = nodeId.ToString(),
				FundingSatoshis = fundingSatoshis,
				ChannelFlags = channelFlags,
				PushMsat = pushMsat,
				FundingFeerateSatByte = fundingFeerateSatByte
			}, cts);

		}

		public async Task<string> Close(string channelId, string shortChannelId = null, Script scriptPubKey = null,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<CloseRequest, string>("close", new CloseRequest()
			{
				ChannelId = channelId,
				ShortChannelId = shortChannelId,
				ScriptPubKey = scriptPubKey?.ToHex()
			}, cts);

		}

		public async Task<string> ForceClose(string channelId, string shortChannelId = null,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<ForceCloseRequest, string>("forceclose", new ForceCloseRequest()
			{
				ChannelId = channelId,
				ShortChannelId = shortChannelId,
			}, cts);
		}

		public async Task<string> UpdateRelayFee(string channelId, int feeBaseMsat, int feeProportionalMillionths,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<UpdateRelayFeeRequest, string>("updaterelayfee", new UpdateRelayFeeRequest()
			{
				ChannelId = channelId,
				FeeBaseMsat = feeBaseMsat,
				FeeProportionalMillionths = feeProportionalMillionths
			}, cts);
		}

		public async Task<List<PeersResponse>> Peers(CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<NoRequestModel, List<PeersResponse>>("peers", NoRequestModel.Instance, cts);
		}

		public async Task<List<ChannelResponse>> Channels(PubKey nodeId = null,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<ChannelsRequest, List<ChannelResponse>>("channels", new ChannelsRequest()
			{
				NodeId = nodeId?.ToString()
			}, cts);
		}

		public async Task<ChannelResponse> Channel(string channelId, CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<ChannelRequest, ChannelResponse>("channel", new ChannelRequest()
			{
				ChannelId = channelId
			}, cts);
		}

		public async Task<List<AllNodesResponse>> AllNodes(CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<NoRequestModel, List<AllNodesResponse>>("allnodes", NoRequestModel.Instance,
				cts);
		}

		public async Task<List<AllChannelsResponse>> AllChannels(CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<NoRequestModel, List<AllChannelsResponse>>("allchannels",
				NoRequestModel.Instance, cts);
		}

		public async Task<List<AllUpdatesResponse>> AllUpdates(CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<NoRequestModel, List<AllUpdatesResponse>>("allupdates",
				NoRequestModel.Instance, cts);
		}

		public async Task<InvoiceResponse> CreateInvoice(string description, long? amountMsat = null,
			int? expireIn = null, BitcoinAddress fallbackAddress = null,
			CancellationToken cts = default)
        {
			return await SendCommandAsync<CreateInvoiceRequest, InvoiceResponse>("createinvoice",
				new CreateInvoiceRequest
				{
					Description = description,
					ExpireIn = expireIn,
					AmountMsat = amountMsat == 0 ? null : amountMsat,
					FallbackAddress = fallbackAddress?.ToString()
				}, cts);
		}

		public async Task<InvoiceResponse> ParseInvoice(string invoice,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<ParseInvoiceRequest, InvoiceResponse>("parseinvoice",
				new ParseInvoiceRequest()
				{
					Invoice = invoice
				}, cts);
		}

		public async Task<string> PayInvoice(PayInvoiceRequest payInvoiceRequest, CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<PayInvoiceRequest, string>("payinvoice", payInvoiceRequest, cts);
		}

		public async Task<string> SendToNode(PubKey nodeId, int amountMsat, string paymentHash, int? maxAttempts = null,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<SendToNodeRequest, string>("sendtonode",
				new SendToNodeRequest()
				{
					NodeId = nodeId.ToString(),
					AmountMsat = amountMsat,
					PaymentHash = paymentHash,
					MaxAttempts = maxAttempts
				}, cts);
		}

		public Task<BitcoinAddress> GetNewAddress(CancellationToken cancellationToken = default)
		{
			return SendCommandAsync<NoRequestModel, BitcoinAddress>("POST", new NoRequestModel(), cancellationToken);
		}

		public async Task<List<GetSentInfoResponse>> GetSentInfo(string paymentHash, string id = null,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<GetSentInfoRequest, List<GetSentInfoResponse>>("getsentinfo",
				new GetSentInfoRequest()
				{
					PaymentHash = paymentHash,
					Id = id
				}, cts);
		}

		public async Task<GetReceivedInfoResponse> GetReceivedInfo(string paymentHash, string invoice = null,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<GetReceivedInfoRequest, GetReceivedInfoResponse>("getreceivedinfo",
				new GetReceivedInfoRequest()
				{
					PaymentHash = paymentHash,
					Invoice = invoice
				}, cts);
		}

		public async Task<InvoiceResponse> GetInvoice(string paymentHash,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<GetReceivedInfoRequest, InvoiceResponse>("getinvoice",
				new GetReceivedInfoRequest()
				{
					PaymentHash = paymentHash
				}, cts);
		}

		public async Task<List<InvoiceResponse>> ListInvoices(DateTime? from = null, DateTime? to = null,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<ListInvoicesRequest, List<InvoiceResponse>>("listinvoices",
				new ListInvoicesRequest()
				{
					From = from?.ToUnixTimestamp(),
					To = to?.ToUnixTimestamp()
				}, cts);
		}

		public async Task<List<InvoiceResponse>> ListPendingInvoices(DateTime? from = null, DateTime? to = null,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<ListInvoicesRequest, List<InvoiceResponse>>("listpendinginvoices",
				new ListInvoicesRequest()
				{
					From = from?.ToUnixTimestamp(),
					To = to?.ToUnixTimestamp()
				}, cts);
		}

		public async Task<List<string>> FindRoute(string invoice, int? amountMsat = null,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<FindRouteRequest, List<string>>("findroute",
				new FindRouteRequest()
				{
					Invoice = invoice,
					AmountMsat = amountMsat
				}, cts);
		}

		public async Task<List<string>> FindRouteToNode(PubKey nodeId, int amountMsat,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<FindRouteToNodeRequest, List<string>>("findroutetonode",
				new FindRouteToNodeRequest()
				{
					NodeId = nodeId.ToString(),
					AmountMsat = amountMsat
				}, cts);
		}

		public async Task<AuditResponse> Audit(DateTime? from = null, DateTime? to = null,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<AuditRequest, AuditResponse>("audit",
				new AuditRequest()
				{
					From = from?.ToUnixTimestamp(),
					To = to?.ToUnixTimestamp()
				}, cts);
		}

		public async Task<List<NetworkFeesResponse>> NetworkFees(DateTime? from = null, DateTime? to = null,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<NetworkFeesRequest, List<NetworkFeesResponse>>("networkfees",
				new NetworkFeesRequest()
				{
					From = from?.ToUnixTimestamp(),
					To = to?.ToUnixTimestamp()
				}, cts);
		}

		public async Task<List<ChannelStatsResponse>> ChannelStats(DateTime? from = null, DateTime? to = null,
			CancellationToken cts = default(CancellationToken))
		{
			return await SendCommandAsync<NoRequestModel, List<ChannelStatsResponse>>("channelstats", NoRequestModel.Instance, cts);
		}


		JsonSerializer _Serializer;
		JsonSerializerSettings _SerializerSettings;
		JsonSerializerSettings SerializerSettings
		{
			get
			{
				if (_SerializerSettings == null)
				{
					var jsonSerializer = new JsonSerializerSettings();
					NBitcoin.JsonConverters.Serializer.RegisterFrontConverters(jsonSerializer, Network);
					_SerializerSettings = jsonSerializer;
				}
				return _SerializerSettings;
			}
		}
		JsonSerializer Serializer
		{
			get
			{
				if (_Serializer == null)
				{
					_Serializer = JsonSerializer.Create(SerializerSettings);
				}
				return _Serializer;
			}
		}

		private async Task<TResponse> SendCommandAsync<TRequest, TResponse>(string method, TRequest data, CancellationToken cts)
		{
			HttpContent content = null;
			if (data != null && !(data is NoRequestModel))
			{
				var jobj = JObject.FromObject(data, Serializer);
				Dictionary<string, string> x = new Dictionary<string, string>();
				foreach (var item in jobj)
				{
					if (item.Value == null || (item.Value.Type == JTokenType.Null))
					{
						continue;
					}
					x.Add(item.Key, item.Value.ToString());
				}
				content = new FormUrlEncodedContent(x.Select(pair => pair));
			}

			var httpRequest = new HttpRequestMessage
			{
				Method = HttpMethod.Post,
				RequestUri = new Uri(_address, method),
				Content = content
			};
			httpRequest.Headers.Accept.Clear();
			httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.Default.GetBytes($":{_password}")));

			var rawResult = await _httpClient.SendAsync(httpRequest, cts);
			var rawJson = await rawResult.Content.ReadAsStringAsync();
			if (!rawResult.IsSuccessStatusCode)
			{
				throw new EclairApiException()
				{
					Error = JsonConvert.DeserializeObject<EclairApiError>(rawJson, SerializerSettings)
				};
			}
			return JsonConvert.DeserializeObject<TResponse>(rawJson, SerializerSettings);
		}


		internal class NoRequestModel
		{
			public static NoRequestModel Instance = new NoRequestModel();
		}

		internal class EclairApiException : Exception
		{
			public EclairApiError Error { get; set; }

			public override string Message => Error?.Error;
		}

		internal class EclairApiError
		{
			public string Error { get; set; }
		}


	}
}
