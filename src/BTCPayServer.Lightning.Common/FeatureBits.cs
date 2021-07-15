using System;

namespace BTCPayServer.Lightning
{
    [Flags]
    public enum FeatureBits
    {
        None = 0,

        /// <summary>
        /// TLVOnionPayloadRequired is a feature bit that indicates a node is
        /// able to decode the new TLV information included in the onion packet.
        /// </summary>
        TLVOnionPayloadRequired = 1 << 8,

        /// <summary>
        /// TLVOnionPayloadOptional is an optional feature bit that indicates a
        /// node is able to decode the new TLV information included in the onion
        /// packet.
        /// </summary>
        TLVOnionPayloadOptional = 1 << 9,

        /// <summary>
        /// PaymentAddrRequired is a required feature bit that signals that a
        /// node requires payment addresses, which are used to mitigate probing
        /// attacks on the receiver of a payment.
        /// </summary>
        PaymentAddrRequired = 1 << 14,

        /// <summary>
        /// PaymentAddrOptional is an optional feature bit that signals that a
        /// node supports payment addresses, which are used to mitigate probing
        /// attacks on the receiver of a payment.
        /// </summary>
        PaymentAddrOptional = 1 << 15,

        /// <summary>
        /// MPPOptional is a required feature bit that signals that the receiver
        /// of a payment requires settlement of an invoice with more than one
        /// HTLC.
        /// </summary>
        MPPRequired = 1 << 16,

        /// <summary>
        /// MPPOptional is an optional feature bit that signals that the receiver
        /// of a payment supports settlement of an invoice with more than one
        /// HTLC.
        /// </summary>
        MPPOptional = 1 << 17
    }
}