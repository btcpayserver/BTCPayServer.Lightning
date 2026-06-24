using BTCPayServer.Lightning.Eclair.JsonConverters;
using BTCPayServer.Lightning.JsonConverters;
using BTCPayServer.Lightning.LNDhub.JsonConverters;
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
            
            // LNDhub: "All amounts are satoshis (int)"
            // https://github.com/BlueWallet/LndHub/blob/master/doc/Send-requirements.md
            var lndhubConverter = new LndHubLightMoneyJsonConverter();
            Assert.Equal(615000, JsonConvert.DeserializeObject<LightMoney>("615", lndhubConverter).MilliSatoshi);
            Assert.Equal("615", JsonConvert.SerializeObject(LightMoney.Satoshis(615), lndhubConverter));
            Assert.Equal("615", JsonConvert.SerializeObject(LightMoney.MilliSatoshis(615021), lndhubConverter));
            Assert.Equal("616", JsonConvert.SerializeObject(LightMoney.MilliSatoshis(615521), lndhubConverter));
        }
    }
}
