using System;
using System.Text;
using NBitcoin;
using NBitcoin.Crypto;

namespace BTCPayServer.Lightning
{
    public class CreateInvoiceParams
    {
        public CreateInvoiceParams(LightMoney amount, string description, TimeSpan expiry)
        {
            if (amount == null)
                throw new ArgumentNullException(nameof(amount));
            if (description == null)
                throw new ArgumentNullException(nameof(description));

            Amount = amount;
            Description = description;
            Expiry = expiry;
        }

        public LightMoney Amount { get; set; }
        public string Description { get; set; }
        public uint256 DescriptionHash
        {
            get
            {
                if (Description is null || !DescriptionHashOnly)
                    return null;
                return new uint256(Hashes.SHA256(Encoding.UTF8.GetBytes(Description)), false);
            }
        }
        public bool DescriptionHashOnly { get; set; }
        public TimeSpan Expiry { get; set; }
        public bool PrivateRouteHints { get; set; }
    }
}
