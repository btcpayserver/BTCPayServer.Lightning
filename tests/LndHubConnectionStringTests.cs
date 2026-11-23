using BTCPayServer.Lightning.LNDhub;
using BTCPayServer.Lightning.LndHub;
using NBitcoin;
using Xunit;

namespace BTCPayServer.Lightning.Tests
{
    public class LndHubConnectionStringTests
    {
        [Theory]
        [InlineData("")]
        [InlineData(";allowinsecure=true")]
        [InlineData(";allowinsecure=false")]
        [InlineData(";allowinsecure=invalid")]
        [InlineData(";certthumbprint=invalid;certfilepath=/nonexistent/tls.cert;allowinsecure=invalid")]
        public void LegacyTlsOptionsAreIgnored(string options)
        {
            foreach (var scheme in new[] { "http", "https" })
            {
                var connectionString = $"type=lndhub;server={scheme}://login:password@localhost:8080/";
                var client = Assert.IsType<LndHubLightningClient>(new LndHubConnectionStringHandler().Create(
                    connectionString + options, Network.RegTest, out var error));

                Assert.Null(error);
                Assert.Equal(connectionString, client.ToString());
            }
        }

        [Theory]
        [InlineData("http")]
        [InlineData("https")]
        public void BlueWalletUrisDoNotEmitLegacyTlsOptions(string scheme)
        {
            var client = Assert.IsType<LndHubLightningClient>(new LndHubConnectionStringHandler().Create(
                $"lndhub://login:password@{scheme}://localhost:8080/",
                Network.RegTest, out var error));

            Assert.Null(error);
            Assert.Equal($"type=lndhub;server={scheme}://login:password@localhost:8080/", client.ToString());
        }
    }
}
