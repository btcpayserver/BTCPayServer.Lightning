using System;

namespace BTCPayServer.Lightning
{
    [Flags]
    public enum FeatureBits
    {
        None = 0,

        // TLVOnionPayloadRequired is a feature bit that indicates a node is
        // able to decode the new TLV information included in the onion packet.
        TLVOnionPayloadRequired = 1 << 8,

        // TLVOnionPayloadOptional is an optional feature bit that indicates a
        // node is able to decode the new TLV information included in the onion
        // packet.
        TLVOnionPayloadOptional = 1 << 9,

        // PaymentAddrRequired is a required feature bit that signals that a
        // node requires payment addresses, which are used to mitigate probing
        // attacks on the receiver of a payment.
        PaymentAddrRequired = 1 << 14,

        // PaymentAddrOptional is an optional feature bit that signals that a
        // node supports payment addresses, which are used to mitigate probing
        // attacks on the receiver of a payment.
        PaymentAddrOptional = 1 << 15,

        // MPPOptional is a required feature bit that signals that the receiver
        // of a payment requires settlement of an invoice with more than one
        // HTLC.
        MPPRequired = 1 << 16,

        // MPPOptional is an optional feature bit that signals that the receiver
        // of a payment supports settlement of an invoice with more than one
        // HTLC.
        MPPOptional = 1 << 17
    }
}