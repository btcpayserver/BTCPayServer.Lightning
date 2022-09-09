using System;

namespace BTCPayServer.Lightning
{
    [Flags]
    public enum FeatureBits : long
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
        MPPOptional = 1 << 17,

        /// <summary>
        // AMPRequired is a required feature bit that signals that the receiver
        // of a payment supports accepts spontaneous payments
        /// </summary>
        AMPRequired = 1 << 30,
        
        /// <summary>
        /// PaymentMetadataRequired is a required bit that denotes that if an
        /// invoice contains metadata, it must be passed along with the payment
        /// htlc(s).
        /// </summary>
        PaymentMetadataRequired = (long) 1 << 48,

        /// <summary>
        // PaymentMetadataOptional is an optional bit that denotes that if an
        // invoice contains metadata, it may be passed along with the payment
        // htlc(s).
        /// </summary>
        PaymentMetadataOptional = (long) 1 << 49
    }
}
