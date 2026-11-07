using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using PulseMirror.Instruments.Usb;

namespace PulseMirror.Instruments.UsbTmc
{
    /// <summary>
    /// SCPI transport implementation talking raw USBTMC (USB Test and Measurement
    /// Class) framing directly over WinUSB bulk endpoints. No VISA, no vendor SDK.
    /// Resource format expected: "usbtmc://VIDxPID" e.g. "usbtmc://1AB1x0588",
    /// or simply "usbtmc" to auto-detect the first matching Rigol device.
    /// </summary>
    public class UsbTmcTransport : IScpiTransport
    {
        private const ushort DefaultVendorId = 0x1AB1;  // Rigol
        private const ushort DefaultProductId = 0x0588; // DS1000E series

        private const byte MsgIdDevDepMsgOut = 1;
        private const byte MsgIdRequestDevDepMsgIn = 2;
        private const byte MsgIdDevDepMsgIn = 2;

        private const int DefaultTimeoutMs = 5000;
        private const int ScreenCaptureTimeoutMs = 15000;

        private WinUsbDevice _device;
        private byte _nextTag = 1;

        public bool IsConnected { get; private set; }

        public Task ConnectAsync(string resourceName)
        {
            return Task.Run(() =>
            {
                ParseVidPid(resourceName, out ushort vid, out ushort pid);

                string devicePath = UsbDeviceFinder.FindDevicePath(vid, pid);
                if (devicePath == null)
                {
                    throw new InvalidOperationException(
                        $"No USBTMC device found for VID=0x{vid:X4} PID=0x{pid:X4}. " +
                        "Make sure the WinUSB driver is bound to the device (e.g. via Zadig).");
                }

                _device = WinUsbDevice.Open(devicePath);
                IsConnected = true;
            });
        }

        public Task WriteLineAsync(string command)
        {
            EnsureConnected();

            return Task.Run(() =>
            {
                byte[] payload = Encoding.ASCII.GetBytes(command + "\n");
                byte tag = NextTag();

                byte[] packet = BuildBulkOutPacket(tag, payload);
                _device.WritePipe(packet);
            });
        }

        public Task<string> ReadLineAsync()
        {
            EnsureConnected();

            return Task.Run(() =>
            {
                byte[] data = ReadDeviceDependentMessage();
                string text = Encoding.ASCII.GetString(data);
                return text.TrimEnd('\r', '\n');
            });
        }

        public Task<byte[]> ReadBlockAsync()
        {
            EnsureConnected();

            return Task.Run(() =>
            {
                // Block reads (e.g. :HARDcopy? screen captures) can take much
                // longer than the default pipe timeout, so extend it for the
                // duration of this read and restore it afterwards.
                _device.SetPipeTimeout(_device.BulkInPipeId, ScreenCaptureTimeoutMs);
                try
                {
                    byte[] raw = ReadDeviceDependentMessage();
                    return ScpiBlockReader.ExtractBlockPayload(raw);
                }
                finally
                {
                    _device.SetPipeTimeout(_device.BulkInPipeId, DefaultTimeoutMs);
                }
            });
        }

        /// <summary>
        /// Sends a REQUEST_DEV_DEP_MSG_IN and reads back one or more Bulk-IN
        /// transfers until the EOM (End Of Message) bit is set.
        /// </summary>
        private byte[] ReadDeviceDependentMessage()
        {
            byte tag = NextTag();
            const int maxTransferSize = 1024 * 1024; // 1 MB - large enough for any DS1000E screen capture.
            byte[] request = BuildRequestDevDepMsgInPacket(tag, maxTransferSize);
            _device.WritePipe(request);

            const int headerSize = 12;

            // Read the whole response in a single Bulk-IN transfer. WinUSB will
            // return as soon as the device signals the end of the transfer (a
            // short packet), so a buffer this size safely covers one full
            // USBTMC message without needing fragile multi-read reassembly.
            byte[] buffer = new byte[headerSize + maxTransferSize];
            int transferred = _device.ReadPipe(buffer);

            if (transferred < headerSize)
            {
                throw new IOException("USBTMC response shorter than the Bulk-IN header.");
            }

            byte msgId = buffer[0];
            if (msgId != MsgIdDevDepMsgIn)
            {
                throw new IOException($"Unexpected USBTMC MsgID in response: {msgId}.");
            }

            uint transferSize = BitConverter.ToUInt32(buffer, 4);
            int dataLength = (int)Math.Min(transferSize, (uint)(transferred - headerSize));

            var payload = new byte[dataLength];
            Array.Copy(buffer, headerSize, payload, 0, dataLength);
            return payload;
        }

        private static byte[] BuildBulkOutPacket(byte tag, byte[] payload)
        {
            int paddedLength = Align4(payload.Length);
            byte[] packet = new byte[12 + paddedLength];

            packet[0] = MsgIdDevDepMsgOut;
            packet[1] = tag;
            packet[2] = (byte)~tag;
            packet[3] = 0; // reserved

            Array.Copy(BitConverter.GetBytes((uint)payload.Length), 0, packet, 4, 4);

            packet[8] = 0x01; // bmTransferAttributes: EOM = 1 (this is the last/only transfer)
            // bytes 9-11 reserved, already zero

            Array.Copy(payload, 0, packet, 12, payload.Length);

            return packet;
        }

        private static byte[] BuildRequestDevDepMsgInPacket(byte tag, int maxTransferSize)
        {
            byte[] packet = new byte[12];

            packet[0] = MsgIdRequestDevDepMsgIn;
            packet[1] = tag;
            packet[2] = (byte)~tag;
            packet[3] = 0; // reserved

            Array.Copy(BitConverter.GetBytes((uint)maxTransferSize), 0, packet, 4, 4);

            packet[8] = 0x00; // TermCharEnabled = 0 (read until EOM, not a specific char)
            // bytes 9-11 reserved

            return packet;
        }

        private byte NextTag()
        {
            byte tag = _nextTag;
            _nextTag = (byte)(_nextTag == 255 ? 1 : _nextTag + 1);
            return tag;
        }

        private static int Align4(int length)
        {
            int remainder = length % 4;
            return remainder == 0 ? length : length + (4 - remainder);
        }

        private static void ParseVidPid(string resourceName, out ushort vid, out ushort pid)
        {
            vid = DefaultVendorId;
            pid = DefaultProductId;

            if (string.IsNullOrWhiteSpace(resourceName))
            {
                return;
            }

            const string prefix = "usbtmc://";
            string trimmed = resourceName.Trim();

            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string idPart = trimmed.Substring(prefix.Length);
                string[] parts = idPart.Split('x', 'X');

                if (parts.Length == 2 &&
                    ushort.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out ushort parsedVid) &&
                    ushort.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out ushort parsedPid))
                {
                    vid = parsedVid;
                    pid = parsedPid;
                }
            }
        }

        private void EnsureConnected()
        {
            if (!IsConnected || _device == null)
            {
                throw new InvalidOperationException("USBTMC transport is not connected.");
            }
        }

        public void Dispose()
        {
            _device?.Dispose();
            _device = null;
            IsConnected = false;
        }
    }
}