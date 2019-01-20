using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.Eclair.Models
{
    public class GetInfoResponse
    {
        [JsonProperty("nodeId")] public string NodeId { get; set; }
        [JsonProperty("alias")] public string Alias { get; set; }
        [JsonProperty("port")] public int Port { get; set; }
        [JsonProperty("chainHash")] public string ChainHash { get; set; }
        [JsonProperty("blockHeight")] public int BlockHeight { get; set; }
    }

    public class ListChannelsResponseItem
    {
        public string nodeId { get; set; }
        public string channelId { get; set; }
        public string state { get; set; }
        public Data data { get; set; }


        public class ChannelKeyPath
        {
            public List<object> path { get; set; }
        }

        public class LocalParams
        {
            public string nodeId { get; set; }
            public ChannelKeyPath channelKeyPath { get; set; }
            public int dustLimitSatoshis { get; set; }
            public long maxHtlcValueInFlightMsat { get; set; }
            public int channelReserveSatoshis { get; set; }
            public int htlcMinimumMsat { get; set; }
            public int toSelfDelay { get; set; }
            public int maxAcceptedHtlcs { get; set; }
            public bool isFunder { get; set; }
            public string defaultFinalScriptPubKey { get; set; }
            public string globalFeatures { get; set; }
            public string localFeatures { get; set; }
        }

        public class RemoteParams
        {
            public string nodeId { get; set; }
            public int dustLimitSatoshis { get; set; }
            public long maxHtlcValueInFlightMsat { get; set; }
            public int channelReserveSatoshis { get; set; }
            public int htlcMinimumMsat { get; set; }
            public int toSelfDelay { get; set; }
            public int maxAcceptedHtlcs { get; set; }
            public string fundingPubKey { get; set; }
            public string revocationBasepoint { get; set; }
            public string paymentBasepoint { get; set; }
            public string delayedPaymentBasepoint { get; set; }
            public string htlcBasepoint { get; set; }
            public string globalFeatures { get; set; }
            public string localFeatures { get; set; }
        }

        public class Spec
        {
            public List<object> htlcs { get; set; }
            public int feeratePerKw { get; set; }
            public int toLocalMsat { get; set; }
            public int toRemoteMsat { get; set; }
        }

        public class PublishableTxs
        {
            public string commitTx { get; set; }
            public List<object> htlcTxsAndSigs { get; set; }
        }

        public class LocalCommit
        {
            public int index { get; set; }
            public Spec spec { get; set; }
            public PublishableTxs publishableTxs { get; set; }
        }

        public class Spec2
        {
            public List<object> htlcs { get; set; }
            public int feeratePerKw { get; set; }
            public int toLocalMsat { get; set; }
            public int toRemoteMsat { get; set; }
        }

        public class RemoteCommit
        {
            public int index { get; set; }
            public Spec2 spec { get; set; }
            public string txid { get; set; }
            public string remotePerCommitmentPoint { get; set; }
        }

        public class LocalChanges
        {
            public List<object> proposed { get; set; }
            public List<object> signed { get; set; }
            public List<object> acked { get; set; }
        }

        public class RemoteChanges
        {
            public List<object> proposed { get; set; }
            public List<object> acked { get; set; }
            public List<object> signed { get; set; }
        }

        public class OriginChannels
        {
        }

        public class CommitInput
        {
            public string outPoint { get; set; }
            public int amountSatoshis { get; set; }
        }

        public class Commitments
        {
            public LocalParams localParams { get; set; }
            public RemoteParams remoteParams { get; set; }
            public int channelFlags { get; set; }
            public LocalCommit localCommit { get; set; }
            public RemoteCommit remoteCommit { get; set; }
            public LocalChanges localChanges { get; set; }
            public RemoteChanges remoteChanges { get; set; }
            public int localNextHtlcId { get; set; }
            public int remoteNextHtlcId { get; set; }
            public OriginChannels originChannels { get; set; }
            public string remoteNextCommitInfo { get; set; }
            public CommitInput commitInput { get; set; }
            public object remotePerCommitmentSecrets { get; set; }
            public string channelId { get; set; }
        }

        public class LastSent
        {
            public string temporaryChannelId { get; set; }
            public string fundingTxid { get; set; }
            public int fundingOutputIndex { get; set; }
            public string signature { get; set; }
        }

        public class Data
        {
            public Commitments commitments { get; set; }
            public LastSent lastSent { get; set; }
        }
    }

    public class CheckInvoiceResponse
    {
        public string prefix { get; set; }
        public object amount { get; set; }
        public int timestamp { get; set; }
        public string nodeId { get; set; }
        public string description { get; set; }
        public string paymentHash { get; set; }
        public int expiry { get; set; }
        public object minFinalCltvExpiry { get; set; }
    }
    
    public class NodeResult
    {
        public string signature { get; set; }
        public string features { get; set; }
        public int timestamp { get; set; }
        public string nodeId { get; set; }
        public string rgbColor { get; set; }
        public string alias { get; set; }
        public List<object> addresses { get; set; }
    }

    public class SendResponse
    {
        public class LastUpdate
        {
            public string signature { get; set; }
            public string chainHash { get; set; }
            public string shortChannelId { get; set; }
            public int timestamp { get; set; }
            public int messageFlags { get; set; }
            public int channelFlags { get; set; }
            public int cltvExpiryDelta { get; set; }
            public int htlcMinimumMsat { get; set; }
            public int feeBaseMsat { get; set; }
            public int feeProportionalMillionths { get; set; }
            public int htlcMaximumMsat { get; set; }
        }

        public class Route
        {
            public string nodeId { get; set; }
            public string nextNodeId { get; set; }
            public LastUpdate lastUpdate { get; set; }
        }

        public int amountMsat { get; set; }
        public string paymentHash { get; set; }
        public string paymentPreimage { get; set; }
        public List<Route> route { get; set; }

    }
}