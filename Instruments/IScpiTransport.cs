using System;
using System.Threading.Tasks;

namespace PulseMirror.Instruments
{
    /// <summary>
    /// Abstracts the physical/transport layer used to exchange SCPI commands
    /// with an instrument (VISA, raw TCP socket, serial, etc.).
    /// </summary>
    public interface IScpiTransport : IDisposable
    {
        bool IsConnected { get; }

        Task ConnectAsync(string resourceName);

        /// <summary>Sends a SCPI command/query terminated with a newline.</summary>
        Task WriteLineAsync(string command);

        /// <summary>Reads a single ASCII line terminated response (e.g. '*IDN?' reply).</summary>
        Task<string> ReadLineAsync();

        /// <summary>
        /// Reads a raw response that may contain an IEEE-488.2 definite-length
        /// block ('#NXXXXXXXX&lt;data&gt;'), returning only the decoded payload bytes.
        /// </summary>
        Task<byte[]> ReadBlockAsync();
    }
}