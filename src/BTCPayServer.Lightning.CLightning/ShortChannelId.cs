using System.Globalization;
using System.Linq;

namespace BTCPayServer.Lightning.CLightning
{
    // clightning-specific representation of channel id
    public class ShortChannelId
    {
        public ShortChannelId (string data)
        {
            var numbers = data.Split(':').Select(s => int.Parse(s)).ToArray();
            BlockHeight = numbers[0];
            BlockIndex = numbers[1];
            TxOutIndex = numbers[2];
        }
        public int BlockHeight { get; set; }
        public int BlockIndex { get; set; }
        public int TxOutIndex { get; set; }

        public override string ToString()
        {
            return BlockHeight.ToString(CultureInfo.InvariantCulture) +
             ":" +
             BlockIndex.ToString(CultureInfo.InvariantCulture) +
             ":" +
             TxOutIndex.ToString(CultureInfo.InvariantCulture);
        }
    }
}