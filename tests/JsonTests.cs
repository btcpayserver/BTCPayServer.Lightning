using BTCPayServer.Lightning.JsonConverters;
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
        }
    }
}