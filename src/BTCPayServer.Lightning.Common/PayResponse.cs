using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Lightning
{
    public enum PayResult
    {
        Ok,
        CouldNotFindRoute,
        Error
    }
    public class PayResponse
    {
        public PayResponse(PayResult result)
        {
            Result = result;
        }
        public PayResult Result
        {
            get; set;
        }
    }
}
