using System;
using System.Threading.Tasks;
using PulseMirror.Instruments;

namespace PulseMirror.Services
{
    public sealed class TriggerService
    {
        private readonly IOscilloscope _oscilloscope;

        public TriggerService(IOscilloscope oscilloscope)
        {
            _oscilloscope = oscilloscope ?? throw new ArgumentNullException(nameof(oscilloscope));
        }

        public Task<TriggerSettings> GetSettingsAsync()
        {
            return _oscilloscope.GetTriggerSettingsAsync();
        }

        public async Task<TriggerSettings> SetTypeAsync(TriggerType type)
        {
            await _oscilloscope.SetTriggerTypeAsync(type);
            return await _oscilloscope.GetTriggerSettingsAsync();
        }

        public async Task<TriggerSettings> SetSourceAsync(TriggerSource source)
        {
            await _oscilloscope.SetTriggerSourceAsync(source);
            return await _oscilloscope.GetTriggerSettingsAsync();
        }

        public async Task<TriggerSettings> SetModeAsync(TriggerMode mode)
        {
            await _oscilloscope.SetTriggerModeAsync(mode);
            return await _oscilloscope.GetTriggerSettingsAsync();
        }

        public async Task<TriggerSettings> SetLevelAsync(double level)
        {
            await _oscilloscope.SetTriggerLevelAsync(level);
            return await _oscilloscope.GetTriggerSettingsAsync();
        }

        public async Task<TriggerSettings> SetSlopeAsync(TriggerSlope slope)
        {
            await _oscilloscope.SetTriggerSlopeAsync(slope);
            return await _oscilloscope.GetTriggerSettingsAsync();
        }

        public async Task<TriggerSettings> SetPulseWidthAsync(double width)
        {
            await _oscilloscope.SetPulseWidthAsync(width);
            return await _oscilloscope.GetTriggerSettingsAsync();
        }

        public async Task<TriggerSettings> SetPulseConditionAsync(PulseCondition condition)
        {
            await _oscilloscope.SetPulseConditionAsync(condition);
            return await _oscilloscope.GetTriggerSettingsAsync();
        }

        public async Task<TriggerSettings> SetVideoStandardAsync(VideoStandard standard)
        {
            await _oscilloscope.SetVideoStandardAsync(standard);
            return await _oscilloscope.GetTriggerSettingsAsync();
        }

        public async Task<TriggerSettings> SetVideoLineAsync(int line)
        {
            await _oscilloscope.SetVideoLineAsync(line);
            return await _oscilloscope.GetTriggerSettingsAsync();
        }
    }
}