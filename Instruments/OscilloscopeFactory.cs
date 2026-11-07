using System;
using System.Threading.Tasks;

namespace PulseMirror.Instruments
{
    /// <summary>
    /// Discovers the instrument identity via '*IDN?' over the appropriate transport
    /// (VISA or raw TCP, chosen automatically from the resource string format) and
    /// returns the matching IOscilloscope implementation.
    /// </summary>
    public static class OscilloscopeFactory
    {
        public static async Task<IOscilloscope> CreateAsync(string resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName))
            {
                throw new ArgumentException("Resource name cannot be empty.", nameof(resourceName));
            }

            IScpiTransport transport = ScpiTransportFactory.Create(resourceName);

            try
            {
                await transport.ConnectAsync(resourceName).ConfigureAwait(false);

                await transport.WriteLineAsync("*IDN?").ConfigureAwait(false);
                string idn = await transport.ReadLineAsync().ConfigureAwait(false);

                var oscilloscope = CreateForIdentity(idn);

                var parts = idn.Split(',');
                string model = parts.Length > 1 ? parts[1].Trim() : "Unknown";
                string serial = parts.Length > 2 ? parts[2].Trim() : "Unknown";

                oscilloscope.AttachTransport(transport, model, serial);

                return oscilloscope;
            }
            catch (Exception ex)
            {
                transport.Dispose();
                throw new InvalidOperationException(
                    $"Failed to identify instrument at '{resourceName}'.", ex);
            }
        }

        private static RigolDS1000EService CreateForIdentity(string idn)
        {
            if (string.IsNullOrWhiteSpace(idn))
            {
                throw new InvalidOperationException("Instrument returned an empty '*IDN?' response.");
            }

            string upper = idn.ToUpperInvariant();

            if (upper.Contains("RIGOL") &&
                (upper.Contains("DS1052E") || upper.Contains("DS1000E") || upper.Contains("DS1102E")))
            {
                return new RigolDS1000EService();
            }

            throw new NotSupportedException($"No oscilloscope driver available for identity string: '{idn}'.");
        }
    }
}