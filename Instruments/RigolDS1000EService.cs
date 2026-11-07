using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PulseMirror.Instruments
{
    /// <summary>
    /// IOscilloscope implementation targeting the Rigol DS1000E series (e.g. DS1052E)
    /// using SCPI commands over a pluggable transport (VISA or raw TCP).
    /// </summary>
    public class RigolDS1000EService : IOscilloscope, IDisposable
    {
        private IScpiTransport _transport;

        // The USBTMC/SCPI transport is strictly sequential: only one command's
        // write+read exchange can be in flight at a time. The capture loop
        // (:DISP:DATA?) and user-initiated commands (e.g. :CHANnel1:SCALe)
        // can be triggered concurrently from different async flows, so every
        // transport access must be serialized through this lock to avoid
        // interleaving bytes from two different SCPI transactions.
        private readonly SemaphoreSlim _transportLock = new SemaphoreSlim(1, 1);

        public bool IsConnected => _transport?.IsConnected ?? false;
        public string ModelName { get; private set; }
        public string SerialNumber { get; private set; }

        /// <summary>
        /// Allows reusing an already-connected transport (e.g. from <see cref="OscilloscopeFactory"/>, 
        /// which needs to query '*IDN?' before knowing which driver to instantiate).
        /// </summary>
        internal void AttachTransport(IScpiTransport transport, string modelName, string serialNumber)
        {
            _transport = transport;
            ModelName = modelName;
            SerialNumber = serialNumber;
        }

        public async Task ConnectAsync(string resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName))
            {
                throw new ArgumentException("Resource name cannot be empty.", nameof(resourceName));
            }

            if (_transport != null && _transport.IsConnected)
            {
                return; // Already connected (e.g. attached by the factory).
            }

            _transport = ScpiTransportFactory.Create(resourceName);

            try
            {
                await _transport.ConnectAsync(resourceName).ConfigureAwait(false);

                string idn = await QueryRawAsync("*IDN?").ConfigureAwait(false);
                var parts = idn.Split(',');
                ModelName = parts.Length > 1 ? parts[1].Trim() : "Unknown";
                SerialNumber = parts.Length > 2 ? parts[2].Trim() : "Unknown";
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to connect to '{resourceName}'.", ex);
            }
        }

        public async Task<byte[]> GetScreenCaptureAsync()
        {
            EnsureConnected();

            await _transportLock.WaitAsync().ConfigureAwait(false);
            try
            {
                try
                {
                    await _transport.WriteLineAsync(":DISP:DATA?").ConfigureAwait(false);
                    return await _transport.ReadBlockAsync().ConfigureAwait(false);
                }
                catch (TimeoutException ex)
                {
                    throw new TimeoutException("Timed out while capturing the oscilloscope screen.", ex);
                }
            }
            finally
            {
                _transportLock.Release();
            }
        }

        public async Task<double> GetChannelScaleAsync(int channel)
        {
            EnsureConnected();
            ValidateChannel(channel);

            string response = await QueryRawAsync($":CHANnel{channel}:SCALe?").ConfigureAwait(false);
            return ParseDouble(response);
        }

        public async Task SetChannelScaleAsync(int channel, double scale)
        {
            EnsureConnected();
            ValidateChannel(channel);

            if (scale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scale), "Vertical scale must be positive.");
            }

            string formattedScale = scale.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            await WriteRawAsync($":CHANnel{channel}:SCALe {formattedScale}").ConfigureAwait(false);
        }

        public async Task<double> GetTimebaseAsync()
        {
            EnsureConnected();

            string response = await QueryRawAsync(":TIMebase:SCALe?").ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[DEBUG] Raw :TIMebase:SCALe? response: '{response}'");
            return ParseDouble(response);
        }

        public async Task SetTimebaseAsync(double scale)
        {
            EnsureConnected();

            if (scale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scale), "Timebase scale must be positive.");
            }

            // Use plain decimal notation (matching SetChannelScaleAsync), not scientific
            // notation - the DS1000E rejects/ignores ":TIMebase:SCALe" values formatted
            // with an exponent, which is why the instrument never actually applied the
            // new timebase and kept reporting a stale/default value.
            string formattedScale = scale.ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture);
            await WriteRawAsync($":TIMebase:SCALe {formattedScale}").ConfigureAwait(false);
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("The oscilloscope is not connected.");
            }
        }

        private static void ValidateChannel(int channel)
        {
            if (channel < 1 || channel > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(channel), "Channel must be between 1 and 4.");
            }
        }

        private async Task<string> QueryRawAsync(string scpiCommand)
        {
            await _transportLock.WaitAsync().ConfigureAwait(false);
            try
            {
                try
                {
                    await _transport.WriteLineAsync(scpiCommand).ConfigureAwait(false);
                    return await _transport.ReadLineAsync().ConfigureAwait(false);
                }
                catch (TimeoutException ex)
                {
                    throw new TimeoutException($"Timed out while querying '{scpiCommand}'.", ex);
                }
            }
            finally
            {
                _transportLock.Release();
            }
        }

        private async Task WriteRawAsync(string scpiCommand)
        {
            await _transportLock.WaitAsync().ConfigureAwait(false);
            try
            {
                try
                {
                    await _transport.WriteLineAsync(scpiCommand).ConfigureAwait(false);
                }
                catch (TimeoutException ex)
                {
                    throw new TimeoutException($"Timed out while writing '{scpiCommand}'.", ex);
                }
            }
            finally
            {
                _transportLock.Release();
            }
        }

        private static double ParseDouble(string response)
        {
            if (!double.TryParse(response.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value))
            {
                throw new FormatException($"Unexpected numeric response from instrument: '{response}'.");
            }

            return value;
        }

        public void Dispose()
        {
            _transport?.Dispose();
            _transport = null;
            _transportLock.Dispose();
        }


    }
}