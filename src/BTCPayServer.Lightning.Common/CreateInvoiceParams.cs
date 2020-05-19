using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Lightning
{
    public class CreateInvoiceParams
    {
        public CreateInvoiceParams(LightMoney amount, string description, TimeSpan expiry)
        {
            Amount = amount;
            Description = description;
            Expiry = expiry;
        }

        public LightMoney Amount { get; set; }
        public string Description { get; set; }
        public TimeSpan Expiry { get; set; }
        public bool PrivateRouteHints { get; set; }
    }
}
