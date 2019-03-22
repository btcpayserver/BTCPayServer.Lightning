using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Lightning
{
    public class LightningNodeInformation
    {
        [Obsolete("Use NodeInfoList.FirstOrDefault() instead")]
        public NodeInfo NodeInfo
        {
            get => NodeInfoList.Count > 0 ? NodeInfoList[0] : null;
        }
        public List<NodeInfo> NodeInfoList { get; set; }
        public int BlockHeight
        {
            get; set;
        }
    }
}
