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
        /// Runs the instrument's waveform auto-setup (':AUTO'), the same as pressing the AUTO key.
        /// </summary>
        public Task AutoSetAsync()
        {
            return WriteAsync(":AUTO");
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
            System.Diagnostics.Debug.WriteLine($"[SCPI] > {scpiCommand}");
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

        public async Task<TriggerSettings> GetTriggerSettingsAsync()
        {
            TriggerType type = await GetTriggerTypeCoreAsync().ConfigureAwait(false);

            var settings = new TriggerSettings
            {
                Type = type,
                Source = await GetTriggerSourceCoreAsync(type).ConfigureAwait(false),
                Mode = await GetTriggerModeCoreAsync().ConfigureAwait(false),
                Level = await GetTriggerLevelCoreAsync(type).ConfigureAwait(false),
                Slope = TriggerSlope.Rising,
                PulseWidth = 0.001,
                PulseCondition = PulseCondition.Equal,
                VideoStandard = VideoStandard.Pal,
                VideoLine = 1
            };

            switch (type)
            {
                case TriggerType.Edge:
                    settings.Slope = await GetTriggerSlopeCoreAsync().ConfigureAwait(false);
                    break;
                case TriggerType.Pulse:
                    settings.PulseWidth = await GetPulseWidthCoreAsync().ConfigureAwait(false);
                    settings.PulseCondition = await GetPulseConditionCoreAsync().ConfigureAwait(false);
                    break;
                case TriggerType.Video:
                    settings.VideoStandard = await GetVideoStandardCoreAsync().ConfigureAwait(false);
                    settings.VideoLine = await GetVideoLineCoreAsync().ConfigureAwait(false);
                    break;
            }

            return settings;
        }

        public Task SetTriggerTypeAsync(TriggerType type)
        {
            return WriteAsync($":TRIGger:MODE {ToScpi(type)}");
        }

        public async Task SetTriggerSourceAsync(TriggerSource source)
        {
            TriggerType type = await GetTriggerTypeCoreAsync().ConfigureAwait(false);
            string section = GetTriggerSection(type);
            await WriteAsync($":TRIGger:{section}:SOURce {ToScpi(source)}").ConfigureAwait(false);
        }

        public Task SetTriggerModeAsync(TriggerMode mode)
        {
            return WriteAsync($":TRIGger:SWEep {ToScpi(mode)}");
        }

        public async Task SetTriggerLevelAsync(double level)
        {
            TriggerType type = await GetTriggerTypeCoreAsync().ConfigureAwait(false);
            string section = GetTriggerSection(type);
            await WriteAsync($":TRIGger:{section}:LEVel {FormatDecimal(level, "0.######")}").ConfigureAwait(false);
        }

        public Task SetTriggerSlopeAsync(TriggerSlope slope)
        {
            return WriteAsync($":TRIGger:EDGe:SLOPe {ToScpi(slope)}");
        }

        public Task SetPulseWidthAsync(double width)
        {
            if (width < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Pulse width must be non-negative.");
            }

            return WriteAsync($":TRIGger:PULSe:WIDTh {FormatDecimal(width, "0.#########")}");
        }

        public Task SetPulseConditionAsync(PulseCondition condition)
        {
            return WriteAsync($":TRIGger:PULSe:WHEN {ToScpi(condition)}");
        }

        public Task SetVideoStandardAsync(VideoStandard standard)
        {
            return WriteAsync($":TRIGger:VIDeo:STANdard {ToScpi(standard)}");
        }

        public Task SetVideoLineAsync(int line)
        {
            if (line < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(line), "Video line must be >= 1.");
            }

            return WriteAsync($":TRIGger:VIDeo:LINE {line}");
        }

        private async Task<TriggerType> GetTriggerTypeCoreAsync()
        {
            string response = await QueryAsync(":TRIGger:MODE?").ConfigureAwait(false);
            return ParseTriggerType(response);
        }

        private async Task<TriggerSource> GetTriggerSourceCoreAsync(TriggerType type)
        {
            string section = GetTriggerSection(type);
            string response = await QueryAsync($":TRIGger:{section}:SOURce?").ConfigureAwait(false);
            return ParseTriggerSource(response);
        }

        private async Task<TriggerMode> GetTriggerModeCoreAsync()
        {
            string response = await QueryAsync(":TRIGger:SWEep?").ConfigureAwait(false);
            return ParseTriggerMode(response);
        }

        private async Task<double> GetTriggerLevelCoreAsync(TriggerType type)
        {
            string section = GetTriggerSection(type);
            string response = await QueryAsync($":TRIGger:{section}:LEVel?").ConfigureAwait(false);
            return ParseDouble(response);
        }

        private async Task<TriggerSlope> GetTriggerSlopeCoreAsync()
        {
            string response = await QueryAsync(":TRIGger:EDGe:SLOPe?").ConfigureAwait(false);
            return ParseTriggerSlope(response);
        }

        private async Task<double> GetPulseWidthCoreAsync()
        {
            string response = await QueryAsync(":TRIGger:PULSe:WIDTh?").ConfigureAwait(false);
            return ParseDouble(response);
        }

        private async Task<PulseCondition> GetPulseConditionCoreAsync()
        {
            string response = await QueryAsync(":TRIGger:PULSe:WHEN?").ConfigureAwait(false);
            return ParsePulseCondition(response);
        }

        private async Task<VideoStandard> GetVideoStandardCoreAsync()
        {
            string response = await QueryAsync(":TRIGger:VIDeo:STANdard?").ConfigureAwait(false);
            return ParseVideoStandard(response);
        }

        private async Task<int> GetVideoLineCoreAsync()
        {
            string response = await QueryAsync(":TRIGger:VIDeo:LINE?").ConfigureAwait(false);
            return ParseInt(response);
        }

        private static string GetTriggerSection(TriggerType type)
        {
            switch (type)
            {
                case TriggerType.Edge:
                    return "EDGe";
                case TriggerType.Pulse:
                    return "PULSe";
                case TriggerType.Video:
                    return "VIDeo";
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), $"Unsupported trigger type: {type}");
            }
        }

        private static int ParseInt(string response)
        {
            if (response == null ||
                !int.TryParse(response.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                throw new FormatException($"Unexpected integer response from instrument: '{response}'.");
            }

            return value;
        }

        private static TriggerType ParseTriggerType(string value)
        {
            string normalized = value == null ? string.Empty : value.Trim().ToUpperInvariant();

            switch (normalized)
            {
                case "EDGE":
                    return TriggerType.Edge;
                case "PULSE":
                    return TriggerType.Pulse;
                case "VIDEO":
                    return TriggerType.Video;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), $"Unknown trigger type: {value}");
            }
        }

        private static TriggerSource ParseTriggerSource(string value)
        {
            string normalized = value == null ? string.Empty : value.Trim().ToUpperInvariant();

            switch (normalized)
            {
                case "CHAN1":
                case "CH1":
                    return TriggerSource.Channel1;
                case "CHAN2":
                case "CH2":
                    return TriggerSource.Channel2;
                case "EXT":
                    return TriggerSource.External;
                case "LINE":
                    return TriggerSource.Line;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), $"Unknown trigger source: {value}");
            }
        }

        private static TriggerMode ParseTriggerMode(string value)
        {
            string normalized = value == null ? string.Empty : value.Trim().ToUpperInvariant();

            switch (normalized)
            {
                case "AUTO":
                    return TriggerMode.Auto;
                case "NORM":
                case "NORMAL":
                    return TriggerMode.Normal;
                case "SING":
                case "SINGLE":
                    return TriggerMode.Single;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), $"Unknown trigger mode: {value}");
            }
        }

        private static TriggerSlope ParseTriggerSlope(string value)
        {
            string normalized = value == null ? string.Empty : value.Trim().ToUpperInvariant();

            switch (normalized)
            {
                case "RIS":
                case "RISING":
                    return TriggerSlope.Rising;
                case "FAL":
                case "FALLING":
                    return TriggerSlope.Falling;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), $"Unsupported trigger slope: {value}");
            }
        }

        private static PulseCondition ParsePulseCondition(string value)
        {
            string normalized = value == null ? string.Empty : value.Trim().ToUpperInvariant();

            switch (normalized)
            {
                case "LESS":
                    return PulseCondition.LessThan;
                case "GREATER":
                    return PulseCondition.GreaterThan;
                case "EQUAL":
                    return PulseCondition.Equal;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), $"Unsupported pulse condition: {value}");
            }
        }

        private static VideoStandard ParseVideoStandard(string value)
        {
            string normalized = value == null ? string.Empty : value.Trim().ToUpperInvariant();

            switch (normalized)
            {
                case "PAL":
                    return VideoStandard.Pal;
                case "NTSC":
                    return VideoStandard.Ntsc;
                case "SECAM":
                    return VideoStandard.Secam;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), $"Unsupported video standard: {value}");
            }
        }

        private static string ToScpi(TriggerType type)
        {
            switch (type)
            {
                case TriggerType.Edge:
                    return "EDGE";
                case TriggerType.Pulse:
                    return "PULSE";
                case TriggerType.Video:
                    return "VIDEO";
                case TriggerType.Slope:
                    return "SLOPE";
                case TriggerType.Pattern:
                    return "PATTERN";
                case TriggerType.Duration:
                    return "DURATION";
                case TriggerType.Alternation:
                    return "ALTERNATION";
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), $"Unsupported trigger type: {type}");
            }
        }

        private static string ToScpi(TriggerSource source)
        {
            switch (source)
            {
                case TriggerSource.Channel1:
                    return "CHAN1";
                case TriggerSource.Channel2:
                    return "CHAN2";
                case TriggerSource.External:
                    return "EXT";
                case TriggerSource.Line:
                    return "LINE";
                default:
                    throw new ArgumentOutOfRangeException(nameof(source), $"Unsupported trigger source: {source}");
            }
        }

        private static string ToScpi(TriggerMode mode)
        {
            switch (mode)
            {
                case TriggerMode.Auto:
                    return "AUTO";
                case TriggerMode.Normal:
                    return "NORM";
                case TriggerMode.Single:
                    return "SING";
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), $"Unsupported trigger mode: {mode}");
            }
        }

        private static string ToScpi(TriggerSlope slope)
        {
            switch (slope)
            {
                case TriggerSlope.Rising:
                    return "RIS";
                case TriggerSlope.Falling:
                    return "FAL";
                default:
                    throw new ArgumentOutOfRangeException(nameof(slope), $"Unsupported trigger slope: {slope}");
            }
        }

        private static string ToScpi(PulseCondition condition)
        {
            switch (condition)
            {
                case PulseCondition.LessThan:
                    return "LESS";
                case PulseCondition.GreaterThan:
                    return "GREATER";
                case PulseCondition.Equal:
                    return "EQUAL";
                default:
                    throw new ArgumentOutOfRangeException(nameof(condition), $"Unsupported pulse condition: {condition}");
            }
        }

        private static string ToScpi(VideoStandard standard)
        {
            switch (standard)
            {
                case VideoStandard.Pal:
                    return "PAL";
                case VideoStandard.Ntsc:
                    return "NTSC";
                case VideoStandard.Secam:
                    return "SECAM";
                default:
                    throw new ArgumentOutOfRangeException(nameof(standard), $"Unsupported video standard: {standard}");
            }
        }
    }
}