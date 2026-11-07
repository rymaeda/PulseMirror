using System;
using System.Text;

namespace PulseMirror.Instruments
{
    /// <summary>
    /// Decodes IEEE-488.2 definite-length arbitrary block data ('#N&lt;len&gt;&lt;payload&gt;').
    /// </summary>
    internal static class ScpiBlockReader
    {
        public static byte[] ExtractBlockPayload(byte[] raw)
        {
            if (raw == null || raw.Length < 2 || raw[0] != (byte)'#')
            {
                return raw ?? Array.Empty<byte>();
            }

            int digitCount = raw[1] - (byte)'0';
            if (digitCount <= 0 || digitCount > 9 || raw.Length < 2 + digitCount)
            {
                return raw;
            }

            string lengthField = Encoding.ASCII.GetString(raw, 2, digitCount);
            if (!int.TryParse(lengthField, out int length))
            {
                return raw;
            }

            int dataStart = 2 + digitCount;
            int available = raw.Length - dataStart;
            int count = Math.Min(length, available);

            var payload = new byte[count];
            Array.Copy(raw, dataStart, payload, 0, count);
            return payload;
        }
    }
}