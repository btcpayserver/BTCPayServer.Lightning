using System;
using System.Globalization;
using System.Linq;

namespace BTCPayServer.Lightning.CLightning
{
    // clightning-specific representation of channel id
    public class ShortChannelId : IEquatable<ShortChannelId>, IComparable<ShortChannelId>
    {
        ShortChannelId (int blockHeight, int blockIndex, int txOutIndex)
        {
            if (blockHeight < 0)
                throw new ArgumentOutOfRangeException(nameof(blockHeight), $"{nameof(blockHeight)} should be more than 0");
            if (blockIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(blockIndex), $"{nameof(blockIndex)} should be more than 0");
            if (txOutIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(txOutIndex), $"{nameof(txOutIndex)} should be more than 0");
            BlockHeight = blockHeight;
            BlockIndex = blockIndex;
            TxOutIndex = txOutIndex;
        }
        public static bool TryParse(string data, out ShortChannelId result)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            result = null;
            var datas = data.Split(new[] { ':','x' }).ToArray();
            if (datas.Length != 3)
                return false;

            if (!int.TryParse(datas[0], out var blockHeight) ||
                !int.TryParse(datas[0], out var blockIndex) ||
                !int.TryParse(datas[0], out var txOutIndex))
                return false;
            if (blockHeight < 0 || blockIndex < 0 || txOutIndex < 0)
                return false;
            result = new ShortChannelId(blockHeight, blockIndex, txOutIndex);
            return true;
        }

        public static ShortChannelId Parse(string data)
        {
            if (TryParse(data, out ShortChannelId result))
                return result;
            throw new FormatException($"Failed to parse data {data} to ShortChannelId");
        }
        public int BlockHeight { get; }
        public int BlockIndex { get; }
        public int TxOutIndex { get; }

        public override string ToString()
            => $"{BlockHeight.ToString(CultureInfo.InvariantCulture)}:{BlockIndex.ToString(CultureInfo.InvariantCulture)}:{TxOutIndex.ToString(CultureInfo.InvariantCulture)}";

        #region IEquatable<ShortChannelId> Members

        public bool Equals(ShortChannelId other)
        {
            if (other == null)
                return false;
            return (BlockHeight == other.BlockHeight && BlockIndex == other.BlockIndex && TxOutIndex == other.TxOutIndex);
        }
        public override bool Equals(object obj)
        {
            ShortChannelId item = obj as ShortChannelId;
            if (item == null)
                return false;
            return this.Equals(item);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + BlockHeight;
                hash = hash * 23 + BlockIndex;
                hash = hash * 23 + TxOutIndex;
                return hash;
            }
        }

        # endregion

        # region IComparable<ShortChannelId>

        public int CompareTo(ShortChannelId other)
        {
            if (other == null)
                return 1;

            var c1 = BlockHeight.CompareTo(other.BlockHeight);
            if (c1 != 0)
                return c1;

            var c2 = BlockIndex.CompareTo(other.BlockIndex);
            if (c2 != 0)
                return c2;

            var c3 = TxOutIndex.CompareTo(other.TxOutIndex);
            if (c3 != 0)
                return c3;

            return 0;
        }

        #endregion
        public static bool operator ==(ShortChannelId a, ShortChannelId b)
        {
            if (Object.ReferenceEquals(a, b))
                return true;
            if (((object)a == null) || ((object)b == null))
                return false;
            return a.Equals(b);
        }
        public static bool operator !=(ShortChannelId a, ShortChannelId b)
            => !(a == b);
    }
}