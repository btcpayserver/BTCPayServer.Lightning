using System;
using System.Globalization;
using System.Text;

namespace BTCPayServer.Lightning.LND
{
    // Check the binary envelope before sending a file as authentication data.
    // Only LND can verify the signature and the meaning of the identifier/caveats.
    // Formats: https://github.com/go-macaroon/macaroon (marshal-v1.go, marshal-v2.go).
    internal static class MacaroonFormat
    {
        public static bool IsValid(byte[] bytes)
        {
            if (bytes.Length == 0)
                return false;
            return bytes[0] == 2 ? IsValidV2(bytes) : IsValidV1(bytes);
        }

        private static bool IsValidV2(byte[] bytes)
        {
            var offset = 1;
            if (!ReadSection(bytes, ref offset, false, out var empty) || empty)
                return false;
            do
            {
                if (!ReadSection(bytes, ref offset, true, out empty))
                    return false;
            } while (!empty);
            return ReadPacket(bytes, ref offset, out var type, out var length)
                   && type == 6 && length == 32 && offset == bytes.Length;
        }

        private static bool ReadSection(byte[] bytes, ref int offset, bool caveat, out bool empty)
        {
            empty = false;
            if (!ReadPacket(bytes, ref offset, out var type, out var length))
                return false;
            if (type == 0)
            {
                empty = true;
                return true;
            }
            var hasLocation = type == 1 && length != 0;
            if (type == 1 && !ReadPacket(bytes, ref offset, out type, out length))
                return false;
            if (type != 2 || !ReadPacket(bytes, ref offset, out type, out length))
                return false;
            if (type == 0)
                return !caveat || !hasLocation;
            return caveat && type == 4
                   && ReadPacket(bytes, ref offset, out type, out length) && type == 0;
        }

        private static bool ReadPacket(byte[] bytes, ref int offset, out int type, out int length)
        {
            type = -1;
            length = 0;
            if (!ReadVarint(bytes, ref offset, out type))
                return false;
            if (type == 0)
                return true;
            if (!ReadVarint(bytes, ref offset, out length) || length > bytes.Length - offset)
                return false;
            offset += length;
            return true;
        }

        private static bool ReadVarint(byte[] bytes, ref int offset, out int value)
        {
            value = 0;
            // Packet lengths and field types must fit within the byte[] bounds.
            for (var shift = 0; shift <= 28 && offset < bytes.Length; shift += 7)
            {
                var b = bytes[offset++];
                if (shift == 28 && b > 7)
                    return false;
                value |= (b & 127) << shift;
                if ((b & 128) == 0)
                    return true;
            }
            return false;
        }

        private static bool IsValidV1(byte[] bytes)
        {
            var offset = 0;
            if (!ReadPacketV1(bytes, ref offset, out var field, out _) || field != "location"
                || !ReadPacketV1(bytes, ref offset, out field, out _) || field != "identifier")
                return false;
            var hasCaveat = false;
            var hasVerificationId = false;
            var hasLocation = false;
            while (ReadPacketV1(bytes, ref offset, out field, out var length))
            {
                switch (field)
                {
                    case "signature":
                        return length == 32 && offset == bytes.Length;
                    case "cid":
                        hasCaveat = true;
                        hasVerificationId = hasLocation = false;
                        break;
                    case "vid":
                        if (!hasCaveat || hasVerificationId)
                            return false;
                        hasVerificationId = true;
                        break;
                    case "cl":
                        if (!hasCaveat || hasLocation)
                            return false;
                        hasLocation = true;
                        break;
                    default:
                        return false;
                }
            }
            return false;
        }

        private static bool ReadPacketV1(byte[] bytes, ref int offset, out string field, out int length)
        {
            field = null;
            length = 0;
            if (bytes.Length - offset < 4
                || !int.TryParse(Encoding.ASCII.GetString(bytes, offset, 4), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out var size)
                || size < 6 || size > bytes.Length - offset || bytes[offset + size - 1] != '\n')
                return false;
            var start = offset + 4;
            var end = offset + size - 1;
            var separator = Array.IndexOf(bytes, (byte)' ', start, end - start);
            if (separator == -1)
                return false;
            field = Encoding.ASCII.GetString(bytes, start, separator - start);
            length = end - separator - 1;
            offset += size;
            return true;
        }
    }
}
