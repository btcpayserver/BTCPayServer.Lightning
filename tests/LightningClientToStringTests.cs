using NBitcoin;
using Xunit;

namespace BTCPayServer.Lightning.Tests
{
    public class LightningClientToStringTests
    {
        [Theory]
        [InlineData("type=clightning;server=unix://test/lightning-rpc")]
        [InlineData("type=clightning;server=http://localhost:3010/;rune=tU-RLjMiDpY2U0o3W1oFowar36RFGpWloPbW9-RuZdo9MA==")]
        [InlineData("type=clightning;server=http://localhost:3010/;runefilepath=/secret/rune")]
        [InlineData("type=lnd-rest;server=https://user:password@localhost:8080/;macaroon=0102")]
        [InlineData("type=lnd-rest;server=https://localhost:8080/;macaroonfilepath=/secret/admin.macaroon")]
        [InlineData("type=eclair;server=http://localhost:8080/;username=user;password=secret")]
        [InlineData("type=lndhub;server=https://login:password@localhost:8080/")]
        [InlineData("type=phoenixd;server=http://localhost:9740/;username=user;password=secret")]
        [InlineData("type=phoenixd;server=http://localhost:9740/;passwordfilepath=/secret/phoenix.pwd")]
        public void ToStringUsesDefaultObjectRepresentation(string connectionString)
        {
            var factory = new LightningClientFactory(Network.RegTest);
            Assert.True(factory.TryCreate(connectionString, out var client, out var error), error);
            Assert.Equal(client.GetType().FullName, client.ToString());
        }
    }
}
