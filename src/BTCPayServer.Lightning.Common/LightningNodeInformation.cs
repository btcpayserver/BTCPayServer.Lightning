using System;
using System.Collections.Generic;

namespace BTCPayServer.Lightning;

public class LightningNodeInformation
{
    [Obsolete("Use NodeInfoList.FirstOrDefault() instead")]
    public NodeInfo NodeInfo => NodeInfoList.Count > 0 ? NodeInfoList[0] : null;

    public List<NodeInfo> NodeInfoList { get; } = new();
    public int BlockHeight { get; set; }
    public string Alias { get; set; }
    public string Color { get; set; }
    public string Version { get; set; }
    public long? PeersCount { get; set; }
    public long? ActiveChannelsCount { get; set; }
    public long? InactiveChannelsCount { get; set; }
    public long? PendingChannelsCount { get; set; }
}
