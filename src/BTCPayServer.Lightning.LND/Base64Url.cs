using System;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Lightning.LND
{
    public static class Converters
    {
        public static string HexStringToBase64UrlString(this string input)
        {
            var decoded = Encoders.Hex.DecodeData(input);
            return Encoders.Base64.EncodeData(decoded)
                .Replace("/", "_")
                .Replace("+", "-");
        }
    }
}
