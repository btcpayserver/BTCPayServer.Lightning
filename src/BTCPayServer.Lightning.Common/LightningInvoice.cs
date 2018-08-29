using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Lightning
{
    public class LightningInvoice
    {
        public string Id
        {
            get; set;
        }
        public string Status
        {
            get; set;
        }
        public string BOLT11
        {
            get; set;
        }
        public DateTimeOffset? PaidAt
        {
            get; set;
        }
        public LightMoney Amount
        {
            get; set;
        }
    }
}
