using System;
using System.IO;
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

            Assert.Equal(expected, request["params"][2].Value<string>());
        }

        [Fact]
        public async Task FundChannelOmitsFeeRateWhenNotSpecified()
        {
            var request = await CaptureFundChannelRequest(null);

            Assert.Equal(2, ((JArray)request["params"]).Count);
        }

        private static async Task<JObject> CaptureFundChannelRequest(FeeRate feeRate)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var client = new CLightningClient(new Uri($"tcp://127.0.0.1:{port}"), Network.RegTest);

                var fundChannelTask = client.FundChannelAsync(new OpenChannelRequest
                {
                    NodeInfo = new NodeInfo(new Key().PubKey, "127.0.0.1", 9735),
                    ChannelAmount = Money.Satoshis(100_000),
                    FeeRate = feeRate
                }, CancellationToken.None);

                using var tcpClient = await listener.AcceptTcpClientAsync();
                using var stream = tcpClient.GetStream();
                using var textReader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
                using var jsonReader = new JsonTextReader(textReader);
                var request = await JObject.LoadAsync(jsonReader);
                using var textWriter = new StreamWriter(stream, new UTF8Encoding(false), 1024, true);
                await textWriter.WriteAsync("{\"jsonrpc\":\"2.0\",\"id\":0,\"result\":{}}");
                await textWriter.FlushAsync();
                await fundChannelTask;
                return request;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
