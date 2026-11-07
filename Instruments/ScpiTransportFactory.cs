using PulseMirror.Instruments.UsbTmc;

namespace PulseMirror.Instruments
{
    /// <summary>
    /// Creates the SCPI transport used to talk to the instrument. Only direct
    /// USBTMC (via WinUSB) is supported - no VISA, no vendor SDKs.
    /// Resource format expected: "usbtmc://VIDxPID" (e.g. "usbtmc://1AB1x0588")
    /// or "usbtmc" to auto-detect the default Rigol DS1000E VID/PID.
    /// </summary>
    public static class ScpiTransportFactory
    {
        public static IScpiTransport Create(string resourceName)
        {
            return new UsbTmcTransport();
        }
    }
}