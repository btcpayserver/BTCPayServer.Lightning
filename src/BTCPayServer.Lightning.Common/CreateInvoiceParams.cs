using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;

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
            if (expiry == null)
                throw new ArgumentNullException(nameof(expiry));

            Amount = amount;
            Description = description;
            Expiry = expiry;
        }
        public CreateInvoiceParams(LightMoney amount, uint256 descriptionHash, TimeSpan expiry)
        {
            if (amount == null)
                throw new ArgumentNullException(nameof(amount));
            if (descriptionHash == null)
                throw new ArgumentNullException(nameof(descriptionHash));
            if (expiry == null)
                throw new ArgumentNullException(nameof(expiry));

            Amount = amount;
            DescriptionHash = descriptionHash;
            Expiry = expiry;
        }

        public LightMoney Amount { get; set; }
        public string Description { get; set; }
        public uint256 DescriptionHash { get; set; }
        public TimeSpan Expiry { get; set; }
        public bool PrivateRouteHints { get; set; }
    }
}
