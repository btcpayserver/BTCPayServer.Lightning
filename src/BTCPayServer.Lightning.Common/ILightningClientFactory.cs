using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Lightning
{
    public interface ILightningClientFactory
    {
        ILightningClient Create(string connectionString);

        bool TryCreate(string connectionString, out ILightningClient client, out string error);
    }
}
