using System;
using System.Threading.Tasks;

namespace PulseMirror.Instruments
{
    public sealed class TriggerSettings
    {
        public TriggerType Type { get; set; }
        public TriggerSource Source { get; set; }
        public TriggerMode Mode { get; set; }
        public double Level { get; set; }
        public TriggerSlope Slope { get; set; }
        public double PulseWidth { get; set; }
        public PulseCondition PulseCondition { get; set; }
        public VideoStandard VideoStandard { get; set; }
        public int VideoLine { get; set; }

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

        public Task SetPulseConditionAsync(PulseCondition condition)
        {
            return WriteAsync($":TRIGger:PULSe:WHEN {ToScpi(condition)}");
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

        private object ToScpi(TriggerSource source)
        {
            throw new NotImplementedException();
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
            // No explicit return needed for Task-returning async method
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

        private static string FormatDecimal(double value, string format)
        {
            return value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static double ParseDouble(string value)
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            throw new FormatException($"Unable to parse '{value}' as a double.");
        }

        private static int ParseInt(string value)
        {
            if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int result))
            {
                return result;
            }
            throw new FormatException($"Unable to parse '{value}' as an integer.");
        }

        private static PulseCondition ParsePulseCondition(string value)
        {
            if (Enum.TryParse(value.Trim(), true, out PulseCondition condition))
            {
                return condition;
            }
            throw new ArgumentException($"Invalid PulseCondition value: {value}", nameof(value));
        }

        private static TriggerMode ParseTriggerMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Response is null or empty.", nameof(value));
            }

            // Normalize the value for comparison
            value = value.Trim().ToUpperInvariant();

            switch (value)
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
            // Example implementation, adjust as needed for your enum values
            switch (value.Trim().ToUpperInvariant())
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

        private static TriggerSource ParseTriggerSource(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Response string is null or empty.", nameof(value));
            }

            value = value.Trim().ToUpperInvariant();

            switch (value)
            {
                case "CHAN1":
                case "CH1":
                    return TriggerSource.Channel1;
                case "CHAN2":
                case "CH2":
                    return TriggerSource.Channel2;
                case "EXT":
                    return TriggerSource.External; // fixed
                case "LINE":
                    return TriggerSource.Line;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), $"Unknown trigger source: {value}");
            }
        }

        private static TriggerType ParseTriggerType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Response is null or empty.", nameof(value));
            }

            switch (value.Trim().ToUpperInvariant())
            {
                case "EDGE":
                case "EDGe":
                    return TriggerType.Edge;
                case "PULSE":
                case "PULSe":
                    return TriggerType.Pulse;
                case "VIDEO":
                case "VIDeo":
                    return TriggerType.Video;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), $"Unknown trigger type: {value}");
            }
        }

        private static VideoStandard ParseVideoStandard(string value)
        {
            if (Enum.TryParse<VideoStandard>(value.Trim(), true, out var result))
            {
                return result;
            }
            throw new ArgumentException($"Invalid video standard value: {value}", nameof(value));
        }

        private Task<string> QueryAsync(string command)
        {
            // Implementation needed: send the SCPI command and return the response as a string.
            throw new NotImplementedException("QueryAsync must be implemented.");
        }

        private Task WriteAsync(string command)
        {
            // Implementation needed: send the SCPI command.
            throw new NotImplementedException("WriteAsync must be implemented.");
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
            if (!System.Enum.IsDefined(typeof(VideoStandard), standard))
            {
                throw new ArgumentOutOfRangeException(nameof(standard), $"Unsupported video standard: {standard}");
            }

            return standard.ToString().ToUpperInvariant();
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
    }


}