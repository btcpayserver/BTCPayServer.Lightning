using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Lightning.Charge
{
    public class CreateInvoiceResponse
    {
        public string PayReq { get; set; }
        public string Id { get; set; }
    }
}
