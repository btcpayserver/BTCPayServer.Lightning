using BTCPayServer.Lightning.Eclair.JsonConverters;
using BTCPayServer.Lightning.JsonConverters;
using BTCPayServer.Lightning.LNDhub.Models;
using Newtonsoft.Json;
using Xunit;

namespace BTCPayServer.Lightning.Tests
{
    public class JsonTests
    {
        [Fact]
        public void CanSerializeDeserializeLightMoney()
        {
            var converter = new LightMoneyJsonConverter();
            var lm = new LightMoney(100);
            var json = JsonConvert.SerializeObject(lm, converter);

            Assert.Equal(lm.MilliSatoshi, JsonConvert.DeserializeObject<LightMoney>(json, converter).MilliSatoshi);
            Assert.Equal(3187032000, JsonConvert.DeserializeObject<LightMoney>("3187032000.0", converter).MilliSatoshi);
            Assert.Equal("123", JsonConvert.SerializeObject(123, converter));

            var eclairConverter = new EclairBtcJsonConverter();
            Assert.Equal(89997146000, JsonConvert.DeserializeObject<LightMoney>("0.89997146", eclairConverter).MilliSatoshi);
        }

        [Fact]
        public void CanDeserializeLndHubTransactionWithMillisatoshiFee()
        {
            var json = "{\"payment_preimage\":\"7e2a5e2f8b3c1d4e6f708192a3b4c5d6e7f8091a2b3c4d5e6f708192a3b4c5d6\"," +
                       "\"payment_hash\":\"8952c75b560e3a688d1fe9decce3402a84e41ff5aecfc936d3f4118e3a73b447\"," +
                       "\"fee_msat\":-1049148,\"type\":\"paid_invoice\",\"fee\":-1049.148," +
                       "\"value\":-198666,\"timestamp\":1791247367,\"memo\":\"refund\"}";
            var tx = JsonConvert.DeserializeObject<TransactionData>(json);

            Assert.NotNull(tx.Value);
            Assert.NotNull(tx.Fee);
            Assert.Equal(-198666000, tx.Value.MilliSatoshi);
            Assert.Equal(-1049148, tx.Fee.MilliSatoshi);
            Assert.DoesNotContain("\"fee_msat\"", JsonConvert.SerializeObject(tx));
        }

        [Fact]
        public void CanDeserializeLegacyLndHubTransactionFee()
        {
            var tx = JsonConvert.DeserializeObject<TransactionData>("{\"fee\":12}");

            Assert.Equal(12000, tx.Fee.MilliSatoshi);
        }

        [Theory]
        [InlineData("{\"fee\":12,\"fee_msat\":12500}")]
        [InlineData("{\"fee_msat\":12500,\"fee\":12}")]
        public void LndHubTransactionPrefersMillisatoshiFee(string json)
        {
            var tx = JsonConvert.DeserializeObject<TransactionData>(json);

            Assert.Equal(12500, tx.Fee.MilliSatoshi);
        }
    }
}
