using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Lightning
{
    public class LightningNodeInformation
    {
        public NodeInfo NodeInfo
        {
            get; set;
        }
        public int BlockHeight
        {
            get; set;
        }
    }
}
