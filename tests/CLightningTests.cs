using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning.CLightning;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayServer.Lightning.Tests
{
    public class CLightningTests
    {
        [Theory]
        [InlineData(1000, "1000perkb")]
        [InlineData(4000, "4000perkb")]
        [InlineData(1001, "1001perkb")]
        public async Task FundChannelSerializesFeeRateInPerKb(long feePerK, string expected)
        {
            var request = await CaptureFundChannelRequest(new FeeRate(Money.Satoshis(feePerK)));

            Assert.Equal(expected, request["params"]["feerate"].Value<string>());
        }

        [Fact]
        public async Task FundChannelOmitsFeeRateWhenNotSpecified()
        {
            var request = await CaptureFundChannelRequest(null);

            var parameters = (JObject)request["params"];
            Assert.Equal(new[] { "id", "amount" }, parameters.Properties().Select(p => p.Name));
        }

        [Fact]
        public async Task SocketListenerWaitsWithoutTimeout()
        {
            var invoice = "{\"label\":\"a\",\"status\":\"paid\",\"pay_index\":1,\"payment_hash\":\"" + new string('1', 64) + "\"}";
            var request = await CaptureRequest(async client =>
            {
                using var listener = await ((ILightningClient)client).Listen();
                await listener.WaitInvoice(CancellationToken.None);
            }, Result(invoice));

            Assert.Equal("waitanyinvoice", request["method"].Value<string>());
            var parameters = Assert.IsType<JObject>(request["params"]);
            Assert.Equal(new[] { "lastpay_index" }, parameters.Properties().Select(p => p.Name));
            Assert.Equal(99999999999, parameters["lastpay_index"].Value<long>());
        }

        [Theory]
        [InlineData(-32602, PayResult.Error)]
        [InlineData(205, PayResult.CouldNotFindRoute)]
        [InlineData(-1, PayResult.Unknown)]
        [InlineData(200, PayResult.Unknown)]
        public async Task SocketPayOnlyFailsWhenNothingWasSent(int code, PayResult expected)
        {
            PayResponse response = null;
            await CaptureRequest(async client => response = await ((ILightningClient)client).Pay(CLightningPayTests.AmountInvoice),
                Error($"{{\"code\":{code},\"message\":\"Failed\"}}"));

            Assert.Equal(expected, response.Result);
        }

        [Theory]
        [InlineData("failed", PayResult.Error)]
        [InlineData("pending", PayResult.Unknown)]
        public async Task SocketDestinationFailureReconcilesPayment(string status, PayResult expected)
        {
            PayResponse response = null;
            await CaptureRequest(async client => response = await ((ILightningClient)client).Pay(CLightningPayTests.AmountInvoice),
                Error("{\"code\":203,\"message\":\"Destination said it doesn't know invoice\"}"),
                Result($"{{\"pays\":[{{\"status\":\"{status}\"}}]}}"));

            Assert.Equal(expected, response.Result);
        }

        // lightningd always sends an integer code. A string or a fraction isn't converted into one.
        [Theory]
        [InlineData("{\"code\":\"-1\",\"message\":\"Failed\"}")]
        [InlineData("{\"code\":-1.25,\"message\":\"Failed\"}")]
        [InlineData("{\"code\":null,\"message\":\"Failed\"}")]
        [InlineData("{\"message\":\"Failed\"}")]
        [InlineData("{\"code\":-1}")]
        public async Task SocketErrorWithoutIntegerCodeIsNotAnRpcError(string error)
        {
            Exception exception = null;
            await CaptureRequest(async client => exception = await Record.ExceptionAsync(() => client.GetInfoAsync()), Error(error));
            Assert.IsType<IOException>(exception);

            // Pay can't tell what happened, so the outcome stays unknown.
            PayResponse response = null;
            await CaptureRequest(async client => response = await ((ILightningClient)client).Pay(CLightningPayTests.AmountInvoice), Error(error));
            Assert.Equal(PayResult.Unknown, response.Result);
        }

        [Fact]
        public async Task SocketPayIsUnknownWhenTheConnectionFails()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var client = new CLightningClient(new Uri($"tcp://127.0.0.1:{port}"), Network.Main);

            var response = await ((ILightningClient)client).Pay(CLightningPayTests.AmountInvoice);

            Assert.Equal(PayResult.Unknown, response.Result);
        }

        private static string Result(string result) => "{\"jsonrpc\":\"2.0\",\"id\":0,\"result\":" + result + "}";
        private static string Error(string error) => "{\"jsonrpc\":\"2.0\",\"id\":0,\"error\":" + error + "}";

        private static Task<JObject> CaptureFundChannelRequest(FeeRate feeRate)
        {
            return CaptureRequest(client => client.FundChannelAsync(new OpenChannelRequest
            {
                NodeInfo = new NodeInfo(new Key().PubKey, "127.0.0.1", 9735),
                ChannelAmount = Money.Satoshis(100_000),
                FeeRate = feeRate
            }, CancellationToken.None));
        }

        private static async Task<JObject> CaptureRequest(Func<CLightningClient, Task> call, string response = null, string lookupResponse = null)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var client = new CLightningClient(new Uri($"tcp://127.0.0.1:{port}"), Network.Main);

                var callTask = call(client);

                using var tcpClient = await listener.AcceptTcpClientAsync();
                using var stream = tcpClient.GetStream();
                using var textReader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
                using var jsonReader = new JsonTextReader(textReader);
                var request = await JObject.LoadAsync(jsonReader);
                using var textWriter = new StreamWriter(stream, new UTF8Encoding(false), 1024, true);
                await textWriter.WriteAsync(response ?? Result("{}"));
                await textWriter.FlushAsync();
                if (lookupResponse != null)
                {
                    using var lookupClient = await listener.AcceptTcpClientAsync();
                    using var lookupStream = lookupClient.GetStream();
                    using var lookupReader = new StreamReader(lookupStream, Encoding.UTF8, false, 1024, true);
                    using var lookupJsonReader = new JsonTextReader(lookupReader);
                    var lookup = await JObject.LoadAsync(lookupJsonReader);
                    Assert.Equal("listpays", lookup["method"].Value<string>());
                    using var lookupWriter = new StreamWriter(lookupStream, new UTF8Encoding(false), 1024, true);
                    await lookupWriter.WriteAsync(lookupResponse);
                    await lookupWriter.FlushAsync();
                }
                await callTask;
                return request;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
