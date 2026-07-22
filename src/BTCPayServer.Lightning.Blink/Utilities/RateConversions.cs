using System;
using BTCPayServer.Lightning.Blink.Models.Responses;

namespace BTCPayServer.Lightning.Blink.Utilities
{
    public class RateConversions
    {
        public static long convertCentsToSats(long cents, PriceInfo btcRate)
        {
            double divisionResult = (double)cents / btcRate.Base;

            return (long)(divisionResult * (long)Math.Pow(10, btcRate.Offset));
        }
    }
}

