using System.Threading.Tasks;

namespace PulseMirror.Instruments
{
    /// <summary>
    /// Abstraction for a remote-controllable digital oscilloscope.
    /// </summary>
    public interface IOscilloscope
    {
        bool IsConnected { get; }
        string ModelName { get; }
        string SerialNumber { get; }

        Task ConnectAsync(string resourceName);

        Task<byte[]> GetScreenCaptureAsync();

        Task<double> GetChannelScaleAsync(int channel);

        Task SetChannelScaleAsync(int channel, double scale);

        Task<bool> GetChannelEnabledAsync(int channel);

        Task SetChannelEnabledAsync(int channel, bool enabled);

        Task<double> GetTimebaseAsync();

        Task SetTimebaseAsync(double scale);

        Task<double> GetChannelOffsetAsync(int channel);

        Task SetChannelOffsetAsync(int channel, double offset);

        /// <summary>
        /// Shows or hides an on-screen measurement on the instrument display.
        /// </summary>
        Task SetMeasurementEnabledAsync(MeasurementType measurement, bool enabled);

        /// <summary>
        /// Unlocks the front panel and returns the instrument to local control.
        /// </summary>
        Task ReturnToLocalAsync();

        /// <summary>
        /// Reads a measurement from the instrument. Returns NaN when the value
        /// cannot be measured (e.g. channel off or no signal).
        /// </summary>
        Task<double> GetMeasurementAsync(int channel, MeasurementQuantity quantity);
    }
}