using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Lightning
{
    public class LightningNodeInformation
    {
        [Obsolete("Use NodeInfoList[0] instead")]
        public NodeInfo NodeInfo
        {
            get => NodeInfoList[0];
        }
        public List<NodeInfo> NodeInfoList { get; set; }
        public int BlockHeight
        {
            get; set;
        }
    }
}
