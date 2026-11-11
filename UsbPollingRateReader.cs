using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace CyberGrip
{
    /// <summary>Reads the interrupt-IN endpoint interval from the USB configuration descriptor.</summary>
    internal static class UsbPollingRateReader
    {
        private static readonly Guid UsbHubInterface = new("F18A0E88-C30C-11D0-8815-00A0C906BED8");
        private const uint DigcfPresent = 0x2;
        private const uint DigcfDeviceInterface = 0x10;
        private const uint IoctlUsbGetNodeInformation = 0x00220408;
        private const uint IoctlUsbGetNodeConnectionInformationEx = 0x00220448;
        private const uint IoctlUsbGetDescriptorFromNodeConnection = 0x00220410;
        private const int InvalidHandleValue = -1;

        public static double? GetPollingRateHz(int vendorId, int productId, string? hidDevicePath)
        {
            if (!OperatingSystem.IsWindows()) return null;
            int? wantedInterface = GetInterfaceNumber(hidDevicePath);
            Guid usbHubInterface = UsbHubInterface;
            IntPtr infoSet = SetupDiGetClassDevs(ref usbHubInterface, null, IntPtr.Zero,
                DigcfPresent | DigcfDeviceInterface);
            if (infoSet == new IntPtr(InvalidHandleValue)) return null;

            try
            {
                var interfaceData = new SpDeviceInterfaceData { cbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                for (uint i = 0; SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref usbHubInterface, i, ref interfaceData); i++)
                {
                    string? hubPath = GetInterfacePath(infoSet, ref interfaceData);
                    if (hubPath == null) continue;
                    using SafeFileHandle hub = CreateFile(hubPath, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (hub.IsInvalid) continue;

                    byte[] nodeInfoInput = new byte[128];
                    byte[] nodeInfo = new byte[128];
                    // USB_HUB_NODE. The full USB_NODE_INFORMATION includes the hub's remove mask.
                    if (!DeviceIoControl(hub, IoctlUsbGetNodeInformation, nodeInfoInput, nodeInfoInput.Length, nodeInfo, nodeInfo.Length, out _, IntPtr.Zero)) continue;
                    // USB_NODE_INFORMATION contains a USB_HUB_DESCRIPTOR at byte 4;
                    // bNbrPorts is its third byte, at offset 6.
                    int portCount = nodeInfo[6];

                    for (int port = 1; port <= portCount; port++)
                    {
                        byte[] connectionInput = new byte[sizeof(int)];
                        BitConverter.GetBytes(port).CopyTo(connectionInput, 0);
                        byte[] connection = new byte[4096];
                        if (!DeviceIoControl(hub, IoctlUsbGetNodeConnectionInformationEx, connectionInput, connectionInput.Length,
                                connection, connection.Length, out _, IntPtr.Zero)) continue;

                        int descriptor = 4;
                        int vid = BitConverter.ToUInt16(connection, descriptor + 8);
                        int pid = BitConverter.ToUInt16(connection, descriptor + 10);
                        if (vid != vendorId || pid != productId) continue;

                        byte[] requestInput = new byte[12];
                        BitConverter.GetBytes(port).CopyTo(requestInput, 0);
                        requestInput[4] = 0x80; // The USB stack fills this in for GET_DESCRIPTOR.
                        requestInput[5] = 0x06;
                        BitConverter.GetBytes((ushort)0x0200).CopyTo(requestInput, 6); // Configuration descriptor
                        BitConverter.GetBytes((ushort)0).CopyTo(requestInput, 8);
                        BitConverter.GetBytes((ushort)4096).CopyTo(requestInput, 10);
                        byte[] request = new byte[12 + 4096];
                        if (!DeviceIoControl(hub, IoctlUsbGetDescriptorFromNodeConnection, requestInput, requestInput.Length,
                                request, request.Length, out _, IntPtr.Zero)) continue;

                        double? rate = FindInterruptInRate(request, wantedInterface);
                        if (rate.HasValue) return rate;
                    }
                }
            }
            catch
            {
                // The descriptor is optional diagnostic information; keep the HID connection usable.
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(infoSet);
            }
            return null;
        }

        private static double? FindInterruptInRate(byte[] data, int? wantedInterface)
        {
            const int start = 12;
            if (data.Length < start + 4) return null;
            int totalLength = BitConverter.ToUInt16(data, start + 2);
            int end = Math.Min(data.Length, start + totalLength);
            int interfaceNumber = -1;
            double? fallback = null;

            for (int offset = start; offset + 2 <= end;)
            {
                int length = data[offset];
                if (length < 2 || offset + length > end) break;
                int type = data[offset + 1];
                if (type == 4 && length >= 9) // Interface descriptor
                    interfaceNumber = data[offset + 2];
                else if (type == 5 && length >= 7 && interfaceNumber >= 0 &&
                         (data[offset + 3] & 0x03) == 0x03 && (data[offset + 2] & 0x80) != 0)
                {
                    int interval = data[offset + 6];
                    if (interval == 0) { offset += length; continue; }
                    double rate = 1000.0 / interval; // CyberGrip's RP2040 USB device is full-speed.
                    if (wantedInterface == interfaceNumber) return rate;
                    fallback = fallback.HasValue ? Math.Max(fallback.Value, rate) : rate;
                }
                offset += length;
            }
            return wantedInterface.HasValue ? null : fallback;
        }

        private static int? GetInterfaceNumber(string? path)
        {
            if (path == null) return null;
            Match match = Regex.Match(path, @"&MI_([0-9A-F]{2})", RegexOptions.IgnoreCase);
            return match.Success ? Convert.ToInt32(match.Groups[1].Value, 16) : null;
        }

        private static string? GetInterfacePath(IntPtr infoSet, ref SpDeviceInterfaceData data)
        {
            SetupDiGetDeviceInterfaceDetail(infoSet, ref data, IntPtr.Zero, 0, out uint required, IntPtr.Zero);
            if (required == 0) return null;
            IntPtr detail = Marshal.AllocHGlobal((int)required);
            try
            {
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref data, detail, required, out _, IntPtr.Zero)) return null;
                return Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
            }
            finally { Marshal.FreeHGlobal(detail); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDeviceInterfaceData { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr infoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr infoSet, ref SpDeviceInterfaceData deviceInterfaceData, IntPtr detailData, uint detailDataSize, out uint requiredSize, IntPtr deviceInfoData);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr infoSet);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode,
            [In] byte[] input, int inputSize, [Out] byte[] output, int outputSize,
            out int bytesReturned, IntPtr overlapped);
    }
}
