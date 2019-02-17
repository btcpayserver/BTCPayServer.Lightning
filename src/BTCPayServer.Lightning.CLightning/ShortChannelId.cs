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
            BlockHeight = blockHeight;
            BlockIndex = blockIndex;
            TxOutIndex = txOutIndex;
        }
        public static bool TryParse(string data, out ShortChannelId result)
        {
            result = null;
            var datas = data.Split(':').ToArray();
            if (datas.Length != 3)
                return false;
            try
            {
                var numbers = datas.Select(s => Int32.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                result = new ShortChannelId(numbers[0], numbers[1], numbers[2]);
                return true;
            }
            catch
            {
                return false;
            }
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
            => BlockHeight.GetHashCode() ^ BlockIndex.GetHashCode() ^ TxOutIndex.GetHashCode();

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