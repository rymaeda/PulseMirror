using System;
using System.Linq;
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
        private static readonly string[] SupportedRigolModels = { "DS1052E", "DS1102E", "DS1000E" };

        public static async Task<IOscilloscope> CreateAsync(string resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName))
            {
                throw new ArgumentException("Resource name cannot be empty.", nameof(resourceName));
            }

            IScpiTransport transport = ScpiTransportFactory.Create(resourceName);
            RigolDS1000EService oscilloscope = null;

            try
            {
                await transport.ConnectAsync(resourceName).ConfigureAwait(false);

                await transport.WriteLineAsync("*IDN?").ConfigureAwait(false);
                string idn = await transport.ReadLineAsync().ConfigureAwait(false);

                InstrumentIdentity identity = InstrumentIdentity.Parse(idn);
                oscilloscope = CreateForIdentity(identity);
                oscilloscope.AttachTransport(transport, identity.Model, identity.Serial);

                return oscilloscope;
            }
            catch (Exception ex)
            {
                // Disposing the service also disposes the transport if it was attached
                // (Dispose is idempotent, so disposing both is safe).
                oscilloscope?.Dispose();
                transport.Dispose();

                throw new InvalidOperationException(
                    $"Failed to identify instrument at '{resourceName}'.", ex);
            }
        }

        private static RigolDS1000EService CreateForIdentity(InstrumentIdentity identity)
        {
            string upper = identity.Raw.ToUpperInvariant();

            if (upper.Contains("RIGOL") && SupportedRigolModels.Any(upper.Contains))
            {
                return new RigolDS1000EService();
            }

            throw new NotSupportedException(
                $"No oscilloscope driver available for identity string: '{identity.Raw}'.");
        }
    }
}