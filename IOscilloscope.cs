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

        Task<double> GetTimebaseAsync();

        Task SetTimebaseAsync(double scale);
    }
}