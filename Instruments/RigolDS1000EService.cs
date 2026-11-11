using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace PulseMirror.Instruments
{
    /// <summary>
    /// IOscilloscope implementation targeting the Rigol DS1000E series (e.g. DS1052E)
    /// using SCPI commands over a pluggable transport (VISA or raw TCP).
    /// </summary>
    public class RigolDS1000EService : IOscilloscope, IDisposable
    {
        private const int MinChannel = 1;
        private const int MaxChannel = 4;

        private IScpiTransport _transport;
        private bool _disposed;

        // The SCPI transport is strictly sequential: only one write+read exchange
        // can be in flight at a time. Every transport access goes through
        // ExecuteAsync, which serializes it with this lock.
        private readonly SemaphoreSlim _transportLock = new SemaphoreSlim(1, 1);

        public bool IsConnected => !_disposed && (_transport?.IsConnected ?? false);
        public string ModelName { get; private set; }
        public string SerialNumber { get; private set; }

        /// <summary>
        /// Allows reusing an already-connected transport (e.g. from <see cref="OscilloscopeFactory"/>, 
        /// which needs to query '*IDN?' before knowing which driver to instantiate).
        /// </summary>
        internal void AttachTransport(IScpiTransport transport, string modelName, string serialNumber)
        {
            ThrowIfDisposed();
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            ModelName = modelName;
            SerialNumber = serialNumber;
        }

        // ---------------------------------------------------------------
        // Connection
        // ---------------------------------------------------------------

        public async Task ConnectAsync(string resourceName)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(resourceName))
            {
                throw new ArgumentException("Resource name cannot be empty.", nameof(resourceName));
            }

            if (_transport != null && _transport.IsConnected)
            {
                return; // Already connected (e.g. attached by the factory).
            }

            var transport = ScpiTransportFactory.Create(resourceName);

            try
            {
                await transport.ConnectAsync(resourceName).ConfigureAwait(false);
                _transport = transport;

                string idn = await QueryAsync("*IDN?").ConfigureAwait(false);
                InstrumentIdentity identity = InstrumentIdentity.Parse(idn);
                ModelName = identity.Model;
                SerialNumber = identity.Serial;
            }
            catch (Exception ex)
            {
                // Do not leave a half-initialized transport behind.
                transport.Dispose();
                _transport = null;
                throw new InvalidOperationException($"Failed to connect to '{resourceName}'.", ex);
            }
        }

        // ---------------------------------------------------------------
        // IOscilloscope
        // ---------------------------------------------------------------

        public Task<byte[]> GetScreenCaptureAsync()
        {
            return ExecuteAsync(
                "capturing the oscilloscope screen",
                async transport =>
                {
                    await transport.WriteLineAsync(":DISP:DATA?").ConfigureAwait(false);
                    return await transport.ReadBlockAsync().ConfigureAwait(false);
                });
        }

        public async Task<double> GetChannelScaleAsync(int channel)
        {
            ValidateChannel(channel);
            string response = await QueryAsync($":CHANnel{channel}:SCALe?").ConfigureAwait(false);
            return ParseDouble(response);
        }

        public Task SetChannelScaleAsync(int channel, double scale)
        {
            ValidateChannel(channel);

            if (scale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scale), "Vertical scale must be positive.");
            }

            return WriteAsync($":CHANnel{channel}:SCALe {FormatDecimal(scale, "0.######")}");
        }

        public async Task<bool> GetChannelEnabledAsync(int channel)
        {
            ValidateChannel(channel);
            string response = await QueryAsync($":CHANnel{channel}:DISPlay?").ConfigureAwait(false);
            return ParseBoolean(response);
        }

        public Task SetChannelEnabledAsync(int channel, bool enabled)
        {
            ValidateChannel(channel);
            return WriteAsync($":CHANnel{channel}:DISPlay {(enabled ? "ON" : "OFF")}");
        }

        public async Task<double> GetChannelOffsetAsync(int channel)
        {
            ValidateChannel(channel);
            string response = await QueryAsync($":CHANnel{channel}:OFFSet?").ConfigureAwait(false);
            return ParseDouble(response);
        }

        public Task SetChannelOffsetAsync(int channel, double offset)
        {
            ValidateChannel(channel);
            return WriteAsync($":CHANnel{channel}:OFFSet {FormatDecimal(offset, "0.######")}");
        }

        public async Task<double> GetTimebaseAsync()
        {
            string response = await QueryAsync(":TIMebase:SCALe?").ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[DEBUG] Raw :TIMebase:SCALe? response: '{response}'");
            return ParseDouble(response);
        }

        public Task SetTimebaseAsync(double scale)
        {
            if (scale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scale), "Timebase scale must be positive.");
            }

            // Plain decimal notation: the DS1000E ignores values formatted with an exponent.
            return WriteAsync($":TIMebase:SCALe {FormatDecimal(scale, "0.#########")}");
        }

        public Task SetMeasurementEnabledAsync(MeasurementType measurement, bool enabled)
        {
            return WriteAsync(BuildMeasurementCommand(measurement, enabled));
        }

        /// <summary>
        /// Releases the front panel lock (':KEY:LOCK DISable') and leaves the
        /// "Rmt" state (':KEY:FORCe', equivalent to pressing the Force key).
        /// </summary>
        public async Task ReturnToLocalAsync()
        {
            await WriteAsync(":KEY:LOCK DISable").ConfigureAwait(false);
            await WriteAsync(":KEY:FORCe").ConfigureAwait(false);
        }

        /// <summary>
        /// Single place that maps a measurement to its SCPI command.
        /// The DS1000E exposes ':MEASure:TOTal' to toggle the full measurement panel.
        /// </summary>
        private static string BuildMeasurementCommand(MeasurementType measurement, bool enabled)
        {
            string state = enabled ? "ON" : "OFF";

            switch (measurement)
            {
                case MeasurementType.All:
                    return $":MEASure:TOTal {state}";
                default:
                    throw new NotSupportedException(
                        $"Measurement '{measurement}' is not supported by this instrument driver.");
            }
        }

        public async Task<double> GetMeasurementAsync(int channel, MeasurementQuantity quantity)
        {
            ValidateChannel(channel);

            string response = await QueryAsync(
                $":MEASure:{GetMeasurementMnemonic(quantity)}? CHANnel{channel}").ConfigureAwait(false);

            // The instrument answers with non-numeric text when the value cannot be measured.
            return double.TryParse(response?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : double.NaN;
        }

        private static string GetMeasurementMnemonic(MeasurementQuantity quantity)
        {
            switch (quantity)
            {
                case MeasurementQuantity.Vpp: return "VPP";
                case MeasurementQuantity.Vmax: return "VMAX";
                case MeasurementQuantity.Vmin: return "VMIN";
                case MeasurementQuantity.Vamplitude: return "VAMPlitude";
                case MeasurementQuantity.Vtop: return "VTOP";
                case MeasurementQuantity.Vbase: return "VBASe";
                case MeasurementQuantity.Vaverage: return "VAVerage";
                case MeasurementQuantity.Vrms: return "VRMS";
                case MeasurementQuantity.Overshoot: return "OVERshoot";
                case MeasurementQuantity.Preshoot: return "PREShoot";
                case MeasurementQuantity.Frequency: return "FREQuency";
                case MeasurementQuantity.Period: return "PERiod";
                case MeasurementQuantity.RiseTime: return "RISetime";
                case MeasurementQuantity.FallTime: return "FALLtime";
                case MeasurementQuantity.PositiveWidth: return "PWIDth";
                case MeasurementQuantity.NegativeWidth: return "NWIDth";
                case MeasurementQuantity.PositiveDuty: return "PDUTycycle";
                case MeasurementQuantity.NegativeDuty: return "NDUTycycle";
                default:
                    throw new NotSupportedException(
                        $"Measurement '{quantity}' is not supported by this instrument driver.");
            }
        }

        // ---------------------------------------------------------------
        // Transport helpers (single place for locking, validation, errors)
        // ---------------------------------------------------------------

        private Task<string> QueryAsync(string scpiCommand)
        {
            return ExecuteAsync(
                $"querying '{scpiCommand}'",
                async transport =>
                {
                    await transport.WriteLineAsync(scpiCommand).ConfigureAwait(false);
                    return await transport.ReadLineAsync().ConfigureAwait(false);
                });
        }

        private Task WriteAsync(string scpiCommand)
        {
            return ExecuteAsync(
                $"writing '{scpiCommand}'",
                async transport =>
                {
                    await transport.WriteLineAsync(scpiCommand).ConfigureAwait(false);
                    return true;
                });
        }

        /// <summary>
        /// Runs one SCPI transaction while holding the transport lock.
        /// </summary>
        private async Task<T> ExecuteAsync<T>(string operation, Func<IScpiTransport, Task<T>> action)
        {
            ThrowIfDisposed();
            EnsureConnected();

            await _transportLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Re-check inside the lock: the transport may have been disposed while waiting.
                var transport = _transport;
                if (_disposed || transport == null)
                {
                    throw new InvalidOperationException("The oscilloscope is not connected.");
                }

                return await action(transport).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException($"Timed out while {operation}.", ex);
            }
            finally
            {
                _transportLock.Release();
            }
        }

        // ---------------------------------------------------------------
        // Validation / parsing
        // ---------------------------------------------------------------

        private void EnsureConnected()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("The oscilloscope is not connected.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RigolDS1000EService));
            }
        }

        private static void ValidateChannel(int channel)
        {
            if (channel < MinChannel || channel > MaxChannel)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(channel), $"Channel must be between {MinChannel} and {MaxChannel}.");
            }
        }

        private static string FormatDecimal(double value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        private static double ParseDouble(string response)
        {
            if (response == null ||
                !double.TryParse(response.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new FormatException($"Unexpected numeric response from instrument: '{response}'.");
            }

            return value;
        }

        /// <summary>
        /// Parses boolean-like SCPI responses. The DS1000E may reply with
        /// "ON"/"OFF" or "1"/"0" depending on firmware/query context.
        /// </summary>
        private static bool ParseBoolean(string response)
        {
            string trimmed = response?.Trim();

            if (string.Equals(trimmed, "ON", StringComparison.OrdinalIgnoreCase) || trimmed == "1")
            {
                return true;
            }

            if (string.Equals(trimmed, "OFF", StringComparison.OrdinalIgnoreCase) || trimmed == "0")
            {
                return false;
            }

            throw new FormatException($"Unexpected boolean response from instrument: '{response}'.");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _transport?.Dispose();
            _transport = null;
            _transportLock.Dispose();
        }
    }
}