using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PulseMirror.Instruments.Usb
{
    /// <summary>
    /// Wraps a WinUSB session (file handle + interface handle + bulk endpoints)
    /// for a single USB device, exposing raw pipe read/write primitives.
    /// </summary>
    public sealed class WinUsbDevice : IDisposable
    {
        private const int DefaultTimeoutMs = 5000;
        private const int ScreenCaptureTimeoutMs = 15000;

        private IntPtr _deviceHandle = IntPtr.Zero;
        private IntPtr _interfaceHandle = IntPtr.Zero;

        public byte BulkInPipeId { get; private set; }
        public byte BulkOutPipeId { get; private set; }
        public ushort BulkInMaxPacketSize { get; private set; }

        public static WinUsbDevice Open(string devicePath)
        {
            if (string.IsNullOrWhiteSpace(devicePath))
            {
                throw new ArgumentException("Device path cannot be empty.", nameof(devicePath));
            }

            IntPtr deviceHandle = NativeMethods.CreateFile(
                devicePath,
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                NativeMethods.FILE_FLAG_OVERLAPPED,
                IntPtr.Zero);

            if (deviceHandle == IntPtr.Zero || deviceHandle.ToInt64() == -1)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"Failed to open USB device at '{devicePath}'.");
            }

            var device = new WinUsbDevice { _deviceHandle = deviceHandle };

            try
            {
                if (!NativeMethods.WinUsb_Initialize(deviceHandle, out IntPtr interfaceHandle))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_Initialize failed.");
                }

                device._interfaceHandle = interfaceHandle;
                device.DiscoverBulkEndpoints();
                device.ConfigureTimeouts();

                return device;
            }
            catch
            {
                device.Dispose();
                throw;
            }
        }

        private void DiscoverBulkEndpoints()
        {
            if (!NativeMethods.WinUsb_QueryInterfaceSettings(
                _interfaceHandle, 0, out NativeMethods.USB_INTERFACE_DESCRIPTOR descriptor))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_QueryInterfaceSettings failed.");
            }

            bool foundIn = false;
            bool foundOut = false;

            for (byte i = 0; i < descriptor.bNumEndpoints; i++)
            {
                if (!NativeMethods.WinUsb_QueryPipe(
                    _interfaceHandle, 0, i, out NativeMethods.WINUSB_PIPE_INFORMATION pipeInfo))
                {
                    continue;
                }

                bool isIn = (pipeInfo.PipeId & NativeMethods.USB_ENDPOINT_DIRECTION_MASK) != 0;

                // PipeType 2 == USB_ENDPOINT_TYPE_BULK
                if (pipeInfo.PipeType != 2)
                {
                    continue;
                }

                if (isIn && !foundIn)
                {
                    BulkInPipeId = pipeInfo.PipeId;
                    BulkInMaxPacketSize = pipeInfo.MaximumPacketSize;
                    foundIn = true;
                }
                else if (!isIn && !foundOut)
                {
                    BulkOutPipeId = pipeInfo.PipeId;
                    foundOut = true;
                }
            }

            if (!foundIn || !foundOut)
            {
                throw new InvalidOperationException(
                    "Could not locate both bulk IN and bulk OUT endpoints on the USBTMC interface.");
            }
        }

        private void ConfigureTimeouts()
        {
            uint timeout = DefaultTimeoutMs;
            NativeMethods.WinUsb_SetPipePolicy(
                _interfaceHandle, BulkInPipeId, NativeMethods.PIPE_TRANSFER_TIMEOUT, sizeof(uint), ref timeout);
            NativeMethods.WinUsb_SetPipePolicy(
                _interfaceHandle, BulkOutPipeId, NativeMethods.PIPE_TRANSFER_TIMEOUT, sizeof(uint), ref timeout);
        }

        public void WritePipe(byte[] buffer)
        {
            if (!NativeMethods.WinUsb_WritePipe(
                _interfaceHandle, BulkOutPipeId, buffer, buffer.Length, out _, IntPtr.Zero))
            {
                int errorCode = Marshal.GetLastWin32Error();
                throw new Win32Exception(errorCode, $"WinUsb_WritePipe failed. (Win32 error code: {errorCode})");
            }
        }

        public int ReadPipe(byte[] buffer)
        {
            if (!NativeMethods.WinUsb_ReadPipe(
                _interfaceHandle, BulkInPipeId, buffer, buffer.Length, out int transferred, IntPtr.Zero))
            {
                int errorCode = Marshal.GetLastWin32Error();

                // ERROR_SEM_TIMEOUT (121): pipe may now be stalled - reset it so
                // subsequent reads don't keep failing with the same error.
                if (errorCode == 121)
                {
                    NativeMethods.WinUsb_ResetPipe(_interfaceHandle, BulkInPipeId);
                }

                throw new Win32Exception(errorCode, $"WinUsb_ReadPipe failed. (Win32 error code: {errorCode})");
            }

            return transferred;
        }

        public void ResetPipes()
        {
            NativeMethods.WinUsb_ResetPipe(_interfaceHandle, BulkInPipeId);
            NativeMethods.WinUsb_ResetPipe(_interfaceHandle, BulkOutPipeId);
        }

        public void SetPipeTimeout(byte pipeId, int timeoutMs)
        {
            uint timeout = (uint)timeoutMs;
            NativeMethods.WinUsb_SetPipePolicy(
                _interfaceHandle, pipeId, NativeMethods.PIPE_TRANSFER_TIMEOUT, sizeof(uint), ref timeout);
        }

        public void Dispose()
        {
            if (_interfaceHandle != IntPtr.Zero)
            {
                NativeMethods.WinUsb_Free(_interfaceHandle);
                _interfaceHandle = IntPtr.Zero;
            }

            if (_deviceHandle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_deviceHandle);
                _deviceHandle = IntPtr.Zero;
            }
        }
    }
}