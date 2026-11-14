using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PulseMirror.Instruments.Usb
{
    /// <summary>
    /// Enumerates WinUSB device interfaces on the system and locates a device
    /// matching the given USB Vendor/Product ID, returning its device path
    /// (e.g. "\\?\usb#vid_1ab1&amp;pid_0588#...#{guid}") for use with CreateFile.
    /// </summary>
    public static class UsbDeviceFinder
    {
        // GUID_DEVINTERFACE_USB_DEVICE - standard WinUSB device interface class.
        private static readonly Guid WinUsbDeviceInterfaceGuid =
            new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");

        public static string FindDevicePath(ushort vendorId, ushort productId)
        {
            foreach (string path in EnumerateDevicePaths())
            {
                string expectedVid = $"vid_{vendorId:x4}";
                string expectedPid = $"pid_{productId:x4}";

                if (path.IndexOf(expectedVid, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    path.IndexOf(expectedPid, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return path;
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateDevicePaths()
        {
            Guid guid = WinUsbDeviceInterfaceGuid;
            IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(
                ref guid, IntPtr.Zero, IntPtr.Zero,
                NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);

            if (deviceInfoSet == IntPtr.Zero || deviceInfoSet.ToInt64() == -1)
            {
                throw new InvalidOperationException("Failed to enumerate USB device interfaces (SetupDiGetClassDevs).");
            }

            try
            {
                int index = 0;
                while (true)
                {
                    var interfaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA
                    {
                        cbSize = Marshal.SizeOf(typeof(NativeMethods.SP_DEVICE_INTERFACE_DATA))
                    };

                    bool success = NativeMethods.SetupDiEnumDeviceInterfaces(
                        deviceInfoSet, IntPtr.Zero, ref guid, index, ref interfaceData);

                    if (!success)
                    {
                        break; // ERROR_NO_MORE_ITEMS or real failure - either way, stop.
                    }

                    string path = GetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData);
                    if (path != null)
                    {
                        yield return path;
                    }

                    index++;
                }
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
        }

        private static string GetDeviceInterfaceDetail(
            IntPtr deviceInfoSet, ref NativeMethods.SP_DEVICE_INTERFACE_DATA interfaceData)
        {
            int requiredSize = 0;

            // First call determines the required buffer size.
            NativeMethods.SetupDiGetDeviceInterfaceDetail(
                deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);

            if (requiredSize <= 0)
            {
                return null;
            }

            IntPtr detailBuffer = Marshal.AllocHGlobal(requiredSize);
            try
            {
                // cbSize for SP_DEVICE_INTERFACE_DETAIL_DATA: 4 (x86) or 8 (x64) due to char[] alignment.
                Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);

                bool success = NativeMethods.SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet, ref interfaceData, detailBuffer, requiredSize, ref requiredSize, IntPtr.Zero);

                if (!success)
                {
                    return null;
                }

                // The device path string starts right after the cbSize (int) field.
                IntPtr pathPtr = IntPtr.Add(detailBuffer, 4);
                return Marshal.PtrToStringAuto(pathPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(detailBuffer);
            }
        }
    }
}