using System;

namespace BTCPayServer.Lightning.LND
{
    public static class Converters
    {
        public static string HexStringToBase64UrlString(this string input)
        {
            return ToBase64UrlString(input.HexStringToHex());
        }

        private static string ToBase64UrlString(byte[] arg)
        {
            if (arg == null)
            {
                throw new ArgumentNullException("arg");
            }

            var s = Convert.ToBase64String(arg);
            return s
                .Replace("/", "_")
                .Replace("+", "-");
        }

        private static byte[] HexStringToHex(this string inputHex)
        {
            var resultantArray = new byte[inputHex.Length / 2];
            for (var i = 0; i < resultantArray.Length; i++)
            {
                resultantArray[i] = Convert.ToByte(inputHex.Substring(i * 2, 2), 16);
            }
            return resultantArray;
        }
    }
}
