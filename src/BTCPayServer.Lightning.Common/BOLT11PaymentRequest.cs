#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using NBitcoin.Crypto;
using System.Collections;
using NBitcoin.DataEncoders;
using System.Diagnostics.CodeAnalysis;

namespace BTCPayServer.Lightning
{
    public class RouteInformation
    {
        internal RouteInformation(BitReader reader, int size)
        {
            var hops = new List<HopInformation>();

            while(size >= HopInformation.Size)
            {
                size -= HopInformation.Size;
                hops.Add(new HopInformation(reader));
            }
            Hops = hops;
        }
        public IReadOnlyList<HopInformation> Hops { get; }
    }

    public class HopInformation
    {
        internal const int Size = 264 + 64 + 32 + 32 + 16;
        internal HopInformation(BitReader reader)
        {
            PubKey = new PubKey(reader.ReadBytes(264 / 8));
            ShortChannelId = Encoders.Hex.EncodeData(reader.ReadBytes(64 / 8));
            FeeBase = LightMoney.FromUnit(reader.ReadULongBE(32), LightMoneyUnit.MilliSatoshi);
            FeeProportional = (decimal)reader.ReadULongBE(32) / 1000000m;
            CLTVExpiryDelay = (ushort)reader.ReadULongBE(16);
        }
        public PubKey PubKey { get; }
        public string ShortChannelId { get; }
        public LightMoney FeeBase { get; }
        public decimal FeeProportional { get; set; }
        public ushort CLTVExpiryDelay { get; }
    }

    public class BOLT11PaymentRequest
    {
        static Dictionary<(string CrytpoCode, ChainName), string> _Prefixes;
        static BOLT11PaymentRequest()
        {
            _Prefixes = new Dictionary<(string CrytpoCode, ChainName), string>();
            _Prefixes.Add(("BTC", ChainName.Mainnet), "lnbc");
            _Prefixes.Add(("BTC", ChainName.Testnet), "lntb");
            _Prefixes.Add(("BTC", ChainName.Regtest), "lnbcrt");
        }
        readonly static char[] digits = new[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
        string _str;
        private BOLT11PaymentRequest(string str, Network network)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            _str = str.ToLowerInvariant().Trim();
            if (str.StartsWith("lightning:", StringComparison.OrdinalIgnoreCase))
                str = str.Substring("lightning:".Length);
            var decoded = InternalBech32Encoder.Instance.DecodeData(str);
            var hrp = decoded.HumanReadablePart.ToLowerInvariant();
            Prefix = hrp;
            MinimumAmount = LightMoney.Zero;
            if (Prefix.Length == 0)
                throw new FormatException("Invalid BOLT11: No prefix");
            ulong amount = 0;
            int firstNumberIndex = -1;
            for (int i = 0; i < Prefix.Length; i++)
            {
                int digit = Array.IndexOf(digits, Prefix[i]);
                if (digit != -1)
                {
                    if (firstNumberIndex == -1)
                        firstNumberIndex = i;
                    amount *= 10;
                    amount += (uint)digit;
                }
                else if (firstNumberIndex != -1)
                {
                    break;
                }
            }

            if (firstNumberIndex != -1)
            {
                LightMoneyUnit unit = LightMoneyUnit.BTC;
                switch (Prefix[Prefix.Length - 1])
                {
                    case 'm':
                        unit = LightMoneyUnit.MilliBTC;
                        break;
                    case 'u':
                        unit = LightMoneyUnit.Micro;
                        break;
                    case 'n':
                        unit = LightMoneyUnit.Nano;
                        break;
                    case 'p':
						unit = LightMoneyUnit.MilliSatoshi;
						if (amount % 10 != 0)
							throw new FormatException("Pico BTC denomination which is not a multiple of 10 is not supported by BTCPayServer.Lightning");
						amount = amount / 10UL;
						break;
                    default:
                        if (Array.IndexOf(digits, Prefix[Prefix.Length - 1]) == -1)
                            throw new FormatException("Invalid BOLT11: invalid amount multiplier");
                        unit = LightMoneyUnit.BTC;
                        break;
                }
                MinimumAmount = LightMoney.FromUnit(amount, unit);
                Prefix = Prefix.Substring(0, firstNumberIndex);
                if (Prefix.Length == 0)
                    throw new FormatException("Invalid BOLT11: No prefix");
            }

            if (_Prefixes.TryGetValue((network.NetworkSet.CryptoCode, network.ChainName), out var expectedPrefix) &&
                expectedPrefix != Prefix)
                throw new FormatException("Invalid BOLT11: Invalid prefix");

            var bitArray = new BitArray(decoded.Data.Length * 5);
            for (int di = 0; di < decoded.Data.Length; di++)
            {
                bitArray.Set(di * 5 + 0, ((decoded.Data[di] >> 4) & 0x01) == 1);
                bitArray.Set(di * 5 + 1, ((decoded.Data[di] >> 3) & 0x01) == 1);
                bitArray.Set(di * 5 + 2, ((decoded.Data[di] >> 2) & 0x01) == 1);
                bitArray.Set(di * 5 + 3, ((decoded.Data[di] >> 1) & 0x01) == 1);
                bitArray.Set(di * 5 + 4, ((decoded.Data[di] >> 0) & 0x01) == 1);
            }

            var reader = new BitReader(bitArray);
            reader.Position = reader.Count - 520 - 30;
            if (reader.Position < 0)
                throw new FormatException("Invalid BOLT11: Invalid size");

            if (!reader.CanConsume(65))
                throw new FormatException("Invalid BOLT11: Invalid size");
            var rs = reader.ReadBytes(64);
            _OriginalFormat = rs;
            if (!ECDSASignature.TryParseFromCompact(rs, out var s) || s is null)
                throw new FormatException("Invalid BOLT11: Invalid ECDSA signature");
            ECDSASignature = s;
            RecoveryId = reader.ReadBytes(1)[0];

            reader.Position = 0;
            Timestamp = Utils.UnixTimeToDateTime(reader.ReadULongBE(35));

            void AssertSize(int c)
            {
                if (!reader.CanConsume(c))
                    throw new FormatException("Invalid BOLT11: invalid size");
            }
            ExpiryDate = Timestamp + TimeSpan.FromHours(1);
            MinFinalCLTVExpiry = 9;
            var fallbackAddresses = new List<BitcoinAddress>();
            var routes = new List<RouteInformation>();
            var features = FeatureBits.None;
            while (reader.Position != reader.Count - 520 - 30)
            {
                AssertSize(5 + 10);
                var tag = (Bolt11FieldType)reader.ReadULongBE(5);
                var size = (int)(reader.ReadULongBE(10) * 5);
                AssertSize(size);
                var afterReadPosition = reader.Position + size;
                switch (tag)
                {
                    case Bolt11FieldType.P:
                        if (size != 52 * 5)
                            break;
                        if (PaymentHash != null)
                            throw new FormatException("Invalid BOLT11: Duplicate 'p'");
                        PaymentHash = new uint256(reader.ReadBytes(32), false);
                        break;

                    case Bolt11FieldType.X:
                        ExpiryDate = Timestamp + TimeSpan.FromSeconds(reader.ReadULongBE(size));
                        break;

                    case Bolt11FieldType.D:
                        var bytesCount = size / 8;
                        var bytes = reader.ReadBytes(bytesCount);
                        try
                        {
                            ShortDescription = UTF8NoBOM.GetString(bytes, 0, bytesCount);
                        }
                        catch
                        {
                        }
                        break;

                    case Bolt11FieldType.S:
                        if (size != 52 * 5)
                            break;
                        if (PaymentSecret != null)
                            throw new FormatException("Invalid BOLT11: Duplicate 's'");
                        PaymentSecret = new uint256(reader.ReadBytes(32), false);
                        break;

                    case Bolt11FieldType.N:
                        if (size != 53 * 5)
                            break;
                        ExplicitPayeePubKey = new PubKey(reader.ReadBytes(33));
                        break;

                    case Bolt11FieldType.C:
                        var value = reader.ReadULongBE(size);
                        if (value > int.MaxValue)
                            break;
                        MinFinalCLTVExpiry = (int)value;
                        break;

                    case Bolt11FieldType.F:
                        if (size < 5)
                            break;
                        var version = reader.ReadULongBE(5);
                        switch (version)
                        {
                            case 0:
                                if (size == 5 + (20 * 8))
                                {
                                    fallbackAddresses.Add(new BitcoinWitPubKeyAddress(new WitKeyId(reader.ReadBytes(20)), network));
                                }
                                else if (size == 5 + (32 * 8) + 4)
                                {
                                    fallbackAddresses.Add(new BitcoinWitScriptAddress(new WitScriptId(reader.ReadBytes(32)), network));
                                }
                                break;
                            case 17:
                                if (size != 5 + (20 * 8))
                                    break;
                                fallbackAddresses.Add(new BitcoinPubKeyAddress(new KeyId(reader.ReadBytes(20)), network));
                                break;
                            case 18:
                                if (size != 5 + (20 * 8))
                                    break;
                                fallbackAddresses.Add(new BitcoinScriptAddress(new ScriptId(reader.ReadBytes(20)), network));
                                break;
                            default:
                                break;
                        }
                        break;

                    case Bolt11FieldType.H:
                        if (size != 52 * 5)
                            break;
                        DescriptionHash = new uint256(reader.ReadBytes(32), false);
                        break;

                    case Bolt11FieldType.R:
                        if (size < 264 + 64 + 32 + 32 + 16)
                            break;
                        var positionBefore = reader.Position;
                        var routeInformation = new RouteInformation(reader, size);
                        var readen = reader.Position - positionBefore;
                        if (size - readen >= 5)
                            break;
                        routes.Add(routeInformation);
                        break;

                    case Bolt11FieldType.Nine:
                        if (size < 5)
                        {
                            break;
                        }

                        // Read which feature bits are set from right-to-left.
                        for (var i = size - 1; i >= 0; i--)
                        {
                            if (reader.Read())
                            {
                                features |= (FeatureBits)(1 << i);
                            }
                        }
                        break;
                }
                var skip = afterReadPosition - reader.Position;
                if (skip < 0)
                    throw new FormatException("Invalid BOLT11: Invalid size");
                reader.Consume(skip);
            }

            reader = new BitReader(bitArray, bitArray.Count - 520 - 30);
            int byteCount = Math.DivRem(reader.Count, 8, out var remainder);
            if (remainder != 0)
                byteCount++;
            var hashedData = UTF8NoBOM.GetBytes(hrp).Concat(reader.ReadBytes(byteCount)).ToArray();
            Hash = new uint256(Hashes.SHA256(hashedData));
            Routes = routes;
            FallbackAddresses = fallbackAddresses;
            FeatureBits = features;
        }

        public bool VerifySignature()
        {
            if (ExplicitPayeePubKey == null)
                return true; // it is useless to verify a signature with the recovered pubkey, it will always succeed
            return ExplicitPayeePubKey.Verify(Hash, ECDSASignature);
        }

        // Bolt11FieldType defines the byte values that correspond to the supported
        // field types of a Bolt11 invoice.
        // The field name is the character representing that 5-bit value in the
        // bech32 string.
        private enum Bolt11FieldType : ulong
        {
            // 256-bit SHA256 payment_hash.
            P = 1,

            // Expiry time in seconds.
            X = 6,

            // Short description of purpose of payment (UTF-8).
            D = 13,

            // This 256-bit secret prevents forwarding nodes from probing the
            // payment recipient.
            S = 16,

            // 33-byte public key of the payee node.
            N = 19,

            // min_final_cltv_expiry to use for the last HTLC in the route.
            C = 24,

            // Fallback on-chain address: for Bitcoin, this starts with
            // a 5-bit version and contains a witness program or P2PKH
            // or P2SH address.
            F = 9,

            // 256-bit description of purpose of payment (SHA256).
            H = 23,

            // One or more entries containing extra routing information
            // for a private route; there may be more than one r field
            //  * pubkey (264 bits)
            //  * short_channel_id (64 bits)
            //  * fee_base_msat (32 bits, big-endian)
            //  * fee_proportional_millionths (32 bits, big-endian)
            //  * cltv_expiry_delta (16 bits, big-endian)
            R = 3,

            // One or more 5-bit values containing features supported or
            // required for receiving this payment.
            Nine = 5
        }

        public int MinFinalCLTVExpiry { get; }

        public uint256 Hash { get; }
        public IReadOnlyList<BitcoinAddress> FallbackAddresses { get; }
        public IReadOnlyList<RouteInformation> Routes { get; }
        /// <summary>
        /// The payee pubkey exposed by the 'n' field. 
        /// In most case, you want to use GetPayeePubKey instead, as this will also try to recover the public key from the signature
        /// </summary>
        public PubKey? ExplicitPayeePubKey { get; }

        /// <summary>
        /// Try to get the signature first from ExplicitPayeePubKey then from the signature
        /// </summary>
        /// <returns>The public key of the payee</returns>
        public PubKey GetPayeePubKey()
        {
            if (ExplicitPayeePubKey != null)
                return ExplicitPayeePubKey;

            return RecoverPayeePubKey(Hash);
        }

        byte[] _OriginalFormat;
        public int RecoveryId { get; set; }
        public ECDSASignature ECDSASignature { get; }

        public PubKey RecoverPayeePubKey(uint256 hash)
        {
            if (hash == null)
                throw new ArgumentNullException(nameof(hash));
            var nbitcoinFormat = new byte[65];
            nbitcoinFormat[0] = (byte)(27 + RecoveryId);
            Array.Copy(_OriginalFormat, 0, nbitcoinFormat, 1, 32);
            Array.Copy(_OriginalFormat, 32, nbitcoinFormat, 33, 32);
            return PubKey.RecoverCompact(hash, nbitcoinFormat).Compress();
        }

        public string Prefix { get; }
        public LightMoney MinimumAmount { get; }

        public DateTimeOffset Timestamp { get; }
        public string? ShortDescription { get; }
        public uint256? DescriptionHash { get; set; }
        public uint256? PaymentHash { get; }
        public uint256? PaymentSecret { get; }
        public DateTimeOffset ExpiryDate { get; }
        public FeatureBits FeatureBits { get; }

        public static bool TryParse(string str, [MaybeNullWhen(false)] out BOLT11PaymentRequest result, Network network)
        {
			if (str is null)
				throw new ArgumentNullException(nameof(str));
			if (network is null)
				throw new ArgumentNullException(nameof(network));
			result = null;
            try
            {
                result = Parse(str, network);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static BOLT11PaymentRequest Parse(string str, Network network)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            return new BOLT11PaymentRequest(str, network);
        }
        static readonly Encoding UTF8NoBOM = new UTF8Encoding(false);
        public bool VerifyDescriptionHash(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            if (DescriptionHash == null)
                throw new InvalidOperationException($"{nameof(DescriptionHash)} ('h' field) is not specified in the BOLT11 object");
            var bytes = UTF8NoBOM.GetBytes(text);
            return new uint256(Hashes.SHA256(bytes), false) == DescriptionHash;
        }

		public override string ToString()
		{
            return _str;
		}
	}
}
