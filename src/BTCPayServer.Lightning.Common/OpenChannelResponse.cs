namespace BTCPayServer.Lightning
{
    public enum OpenChannelResult
    {
        Ok,
        CannotAffordFunding,
        PeerNotConnected,
        NeedMoreConf,
        AlreadyExists,
    }

    public class OpenChannelResponse
    {
        public OpenChannelResponse(OpenChannelResult result)
        {
            Result = result;
        }
        public OpenChannelResult Result
        {
            get; set;
        }
    }
}
