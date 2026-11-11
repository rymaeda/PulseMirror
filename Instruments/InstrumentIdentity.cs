using System;

namespace PulseMirror.Instruments
{
    /// <summary>
    /// Parsed '*IDN?' response: "manufacturer,model,serial,firmware".
    /// </summary>
    internal sealed class InstrumentIdentity
    {
        private const string Unknown = "Unknown";

        public string Raw { get; private set; }
        public string Manufacturer { get; private set; }
        public string Model { get; private set; }
        public string Serial { get; private set; }

        public static InstrumentIdentity Parse(string idn)
        {
            if (string.IsNullOrWhiteSpace(idn))
            {
                throw new InvalidOperationException("Instrument returned an empty '*IDN?' response.");
            }

            string[] parts = idn.Split(',');

            return new InstrumentIdentity
            {
                Raw = idn,
                Manufacturer = parts.Length > 0 ? parts[0].Trim() : Unknown,
                Model = parts.Length > 1 ? parts[1].Trim() : Unknown,
                Serial = parts.Length > 2 ? parts[2].Trim() : Unknown
            };
        }
    }
}