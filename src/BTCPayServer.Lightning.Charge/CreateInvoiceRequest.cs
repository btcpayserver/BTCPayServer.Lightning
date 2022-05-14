using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Lightning.Charge
{
    public class CreateInvoiceRequest
    {
        public LightMoney Amount { get; set; }
        public TimeSpan Expiry { get; set; }
        public string Description { get; set; }
    }
}
