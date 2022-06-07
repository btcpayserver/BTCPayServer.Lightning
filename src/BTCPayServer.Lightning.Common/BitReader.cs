using System.Collections;
using System.Text;

namespace BTCPayServer.Lightning
{
    class BitReader
    {
        readonly BitArray _array;
        public BitReader(BitArray array)
        {
            _array = array;
            Count = array.Count;
        }
        public BitReader(BitArray array, int bitCount)
        {
            _array = array;
            Count = bitCount;
        }

        public int Position { get; set; }

        public int Count { get; }

        public bool Read()
        {
            var v = _array.Get(Position);
            Position++;
            return v;
        }

        public ulong ReadULongBE(int bitCount)
        {
            ulong value = 0;
            for (int i = 0; i < bitCount; i++)
            {
                var v = Read() ? 1U : 0U;
                value += (v << (bitCount - i - 1));
            }
            return value;
        }

        public byte[] ReadBytes(int byteSize)
        {
            byte[] bytes = new byte[byteSize];
            int maxRead = Count - Position;
            for (int byteIndex = 0; byteIndex < byteSize && maxRead != 0; byteIndex++)
            {
                byte value = 0;
                for (int i = 0; i < 8; i++)
                {
                    var v = Read() ? 1U : 0U;
                    value += (byte)(v << (8 - i - 1));
                    maxRead--;
                    if (maxRead == 0)
                        break;
                }
                bytes[byteIndex] = value;
            }
            return bytes;
        }

        public void Consume(int count)
        {
            Position += count;
        }

        public bool CanConsume(int bitCount)
        {
            return Position + bitCount <= Count;
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder(_array.Length);
            for (int i = 0; i < Count; i++)
            {
                if (i != 0 && i % 8 == 0)
                    builder.Append(' ');
                builder.Append(_array.Get(i) ? "1" : "0");
            }
            return builder.ToString();
        }
    }
}
