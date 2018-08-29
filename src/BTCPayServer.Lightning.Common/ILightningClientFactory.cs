using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Lightning
{
    public interface ILightningClientFactory
    {
        ILightningClient Create(string connectionString);
    }
}
