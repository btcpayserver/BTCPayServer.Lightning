#if NETSTANDARD
using NBitcoin.DataEncoders;
#else
using System;
#endif

namespace BTCPayServer.Lightning;

public static class ConvertHelper
{
    public static byte[] FromHexString(string hex)
    {
#if NETSTANDARD
        return Encoders.Hex.DecodeData(hex);
#else
         return Convert.FromHexString(hex);
#endif
    }

    public static string ToHexString(byte[] data)
    {
#if NETSTANDARD
        return Encoders.Hex.EncodeData(data).ToLowerInvariant();;
#else
         return Convert.ToHexString(data).ToLowerInvariant();
#endif
    }
}
