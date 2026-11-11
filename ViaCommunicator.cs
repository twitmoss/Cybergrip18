using System;
using System.Collections.Generic;
using System.Linq;
using HidSharp;

namespace CyberGrip
{
    public class ViaCommunicator
    {
        public const int VendorId = 0xFEED;
        public const int ProductId = 0x5A21;
        public const ushort ViaUsagePage = 0xFF60;

        // VIA Protocol Commands
        private const byte CMD_GET_PROTOCOL_VERSION = 0x01;
        private const byte CMD_GET_KEYCODE = 0x04;          // id_dynamic_keymap_get_keycode
        private const byte CMD_SET_KEYCODE = 0x05;          // id_dynamic_keymap_set_keycode
        private const byte CMD_EEPROM_RESET = 0x07;         // id_dynamic_keymap_reset
        private const byte CMD_GET_LAYER_COUNT = 0x11;      // id_dynamic_keymap_get_layer_count

        // VIA / Vial Matrix Tester
        private const byte CMD_GET_KEYBOARD_VALUE = 0x02;
        private const byte VIA_SWITCH_MATRIX_STATE = 0x03;
        private const byte CYBERGRIP_JOYSTICK_SETTINGS = 0x40;
        private const byte CYBERGRIP_JOYSTICK_AXES = 0x41;
        private const byte CYBERGRIP_BOOTLOADER_REQUEST = 0x42;
        private const byte CYBERGRIP_BOOTLOADER_MAGIC_0 = 0xC7;
        private const byte CYBERGRIP_BOOTLOADER_MAGIC_1 = 0x18;
        private const byte CYBERGRIP_BOOTLOADER_ACK = 0xAC;

        private HidDevice? _matrixDevice;
        private HidStream? _matrixStream;
        private readonly object _streamLock = new();

        public bool[] GetMatrixStates(byte maxKeys = 19)
        {
            bool[] states = new bool[maxKeys];

            try
            {
                if (_matrixStream == null)
                    return states;

                byte[] request = new byte[33];
                request[0] = 0x00;
                request[1] = CMD_GET_KEYBOARD_VALUE;
                request[2] = VIA_SWITCH_MATRIX_STATE;

                byte[] response;
                lock (_streamLock)
                {
                    _matrixStream.Write(request, 0, request.Length);
                    response = _matrixStream.Read();
                }

                if (response == null || response.Length < 6)
                    return states;

                const int rows = 1;
                const int cols = 19;
                const int rowSize = 3;

                for (int row = 0; row < rows; row++)
                {
                    int rowDataStart = 3 + (row * rowSize);

                    for (int col = 0; col < cols && col < maxKeys; col++)
                    {
                        int colByte = rowSize - 1 - (col / 8);
                        int colBit = col % 8;

                        states[col] =
                            (response[rowDataStart + colByte] &
                             (1 << colBit)) != 0;
                    }
                }
            }
            catch
            {
                // Temporary HID errors simply produce an all-false state.
            }

            return states;
        }

        public (bool Success, bool[] States) GetMatrixStatesWithResult(byte maxKeys = 19)
        {
            bool[] states = new bool[maxKeys];

            try
            {
                if (_matrixStream == null)
                    return (false, states);

                byte[] request = new byte[33];
                request[0] = 0x00;
                request[1] = CMD_GET_KEYBOARD_VALUE;
                request[2] = VIA_SWITCH_MATRIX_STATE;

                byte[] response;
                lock (_streamLock)
                {
                    _matrixStream.Write(request, 0, request.Length);
                    response = _matrixStream.Read();
                }

                if (response == null || response.Length < 6)
                    return (false, states);

                const int rows = 1;
                const int cols = 19;
                const int rowSize = 3;

                for (int row = 0; row < rows; row++)
                {
                    int rowDataStart = 3 + (row * rowSize);

                    for (int col = 0; col < cols && col < maxKeys; col++)
                    {
                        int colByte = rowSize - 1 - (col / 8);
                        int colBit = col % 8;

                        states[col] =
                            (response[rowDataStart + colByte] &
                             (1 << colBit)) != 0;
                    }
                }

                return (true, states);
            }
            catch
            {
                return (false, states);
            }
        }

        public bool StartMatrixTest()
        {
            try
            {
                StopMatrixTest();

                _matrixDevice = GetTargetDevice();

                if (_matrixDevice == null)
                    return false;

                if (!_matrixDevice.TryOpen(out _matrixStream))
                {
                    _matrixDevice = null;
                    return false;
                }

                _matrixStream.ReadTimeout = 100;

                return true;
            }
            catch
            {
                StopMatrixTest();
                return false;
            }
        }

        public void StopMatrixTest()
        {
            try
            {
                _matrixStream?.Dispose();
            }
            catch
            {
            }

            _matrixStream = null;
            _matrixDevice = null;
        }

        private HidDevice? GetTargetDevice()
        {
            return DeviceList.Local
                .GetHidDevices(VendorId, ProductId)
                .FirstOrDefault(d => HasViaUsagePage(d));
        }

        public double? GetUsbPollingRateHz()
        {
            HidDevice? device = _matrixDevice ?? GetTargetDevice();
            return device == null
                ? null
                : UsbPollingRateReader.GetPollingRateHz(VendorId, ProductId, device.DevicePath);
        }

        public (bool Success, bool InvertX, bool InvertY) GetJoystickInversion(byte layer)
        {
            (bool Success, byte Layer, byte Flags) response = ExchangeJoystickInversion(layer, 0xFF);
            return (response.Success, (response.Flags & 0x01) != 0, (response.Flags & 0x02) != 0);
        }

        public bool SetJoystickInversion(byte layer, bool invertX, bool invertY)
        {
            byte flags = (byte)((invertX ? 0x01 : 0) | (invertY ? 0x02 : 0));
            var response = ExchangeJoystickInversion(layer, flags);
            return response.Success && response.Flags == flags;
        }

        public (bool Success, short X, short Y) GetJoystickAxes()
        {
            byte[] request = new byte[33];
            request[0] = 0x00;
            request[1] = CMD_GET_KEYBOARD_VALUE;
            request[2] = CYBERGRIP_JOYSTICK_AXES;

            try
            {
                if (_matrixStream != null)
                {
                    lock (_streamLock)
                    {
                        _matrixStream.Write(request, 0, request.Length);
                        return ParseJoystickAxesResponse(_matrixStream.Read());
                    }
                }

                HidDevice? device = GetTargetDevice();
                if (device == null || !device.TryOpen(out HidStream stream)) return (false, 0, 0);
                using (stream)
                {
                    stream.ReadTimeout = 500;
                    stream.Write(request, 0, request.Length);
                    return ParseJoystickAxesResponse(stream.Read());
                }
            }
            catch
            {
                return (false, 0, 0);
            }
        }

        private static (bool Success, short X, short Y) ParseJoystickAxesResponse(byte[] response)
        {
            if (response.Length < 7 || response[1] != CMD_GET_KEYBOARD_VALUE || response[2] != CYBERGRIP_JOYSTICK_AXES)
                return (false, 0, 0);

            short x = unchecked((short)((response[3] << 8) | response[4]));
            short y = unchecked((short)((response[5] << 8) | response[6]));
            return (true, x, y);
        }

        private (bool Success, byte Layer, byte Flags) ExchangeJoystickInversion(byte layer, byte flags)
        {
            byte[] request = new byte[33];
            request[0] = 0x00;
            request[1] = CMD_GET_KEYBOARD_VALUE;
            request[2] = CYBERGRIP_JOYSTICK_SETTINGS;
            request[3] = layer;
            request[4] = flags;

            try
            {
                if (_matrixStream != null)
                {
                    lock (_streamLock)
                    {
                        _matrixStream.Write(request, 0, request.Length);
                        byte[] response = _matrixStream.Read();
                        bool success = response.Length >= 5 && response[1] == CMD_GET_KEYBOARD_VALUE &&
                            response[2] == CYBERGRIP_JOYSTICK_SETTINGS && response[3] == layer;
                        return (success, success ? response[3] : (byte)0, success ? response[4] : (byte)0);
                    }
                }

                HidDevice? device = GetTargetDevice();
                if (device == null || !device.TryOpen(out HidStream stream)) return (false, 0, 0);
                using (stream)
                {
                    stream.ReadTimeout = 500;
                    stream.Write(request, 0, request.Length);
                    byte[] response = stream.Read();
                    bool success = response.Length >= 5 && response[1] == CMD_GET_KEYBOARD_VALUE &&
                        response[2] == CYBERGRIP_JOYSTICK_SETTINGS && response[3] == layer;
                    return (success, success ? response[3] : (byte)0, success ? response[4] : (byte)0);
                }
            }
            catch
            {
                return (false, 0, 0);
            }
        }

        public bool IsDevicePresent() => GetTargetDevice() != null;

        public bool RequestBootloader()
        {
            HidDevice? device = GetTargetDevice();
            if (device == null || !device.TryOpen(out HidStream stream))
                return false;

            using (stream)
            {
                stream.ReadTimeout = 1000;
                byte[] request = new byte[33];
                request[0] = 0x00;
                request[1] = CMD_GET_KEYBOARD_VALUE;
                request[2] = CYBERGRIP_BOOTLOADER_REQUEST;
                request[3] = CYBERGRIP_BOOTLOADER_MAGIC_0;
                request[4] = CYBERGRIP_BOOTLOADER_MAGIC_1;

                stream.Write(request, 0, request.Length);
                byte[] response = stream.Read();
                return response.Length >= 5 &&
                    response[1] == CMD_GET_KEYBOARD_VALUE &&
                    response[2] == CYBERGRIP_BOOTLOADER_REQUEST &&
                    response[3] == CYBERGRIP_BOOTLOADER_ACK &&
                    response[4] == CYBERGRIP_BOOTLOADER_MAGIC_1;
            }
        }

        private bool HasViaUsagePage(HidDevice device)
        {
            try
            {
                var reportDescriptor = device.GetReportDescriptor();
                foreach (var deviceItem in reportDescriptor.DeviceItems)
                {
                    foreach (uint usage in deviceItem.Usages.GetAllValues())
                    {
                        if ((usage >> 16) == ViaUsagePage)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public ushort GetProtocolVersion()
        {
            var device = GetTargetDevice();
            if (device == null || !device.TryOpen(out HidStream stream)) return 0;

            using (stream)
            {
                stream.ReadTimeout = 1000;
                byte[] req = new byte[33];
                req[0] = 0x00; // Report ID
                req[1] = CMD_GET_PROTOCOL_VERSION;

                stream.Write(req);
                byte[] resp = stream.Read();

                if (resp == null || resp.Length < 4) return 0;

                // resp[0] = Report ID (0x00)
                // resp[1] = Command ID (0x01)
                // resp[2] = Version High Byte
                // resp[3] = Version Low Byte
                return (ushort)((resp[2] << 8) | resp[3]);
            }
        }

        public byte GetLayerCount()
        {
            var device = GetTargetDevice();
            if (device == null || !device.TryOpen(out HidStream stream)) return 0;

            using (stream)
            {
                stream.ReadTimeout = 1000;
                byte[] req = new byte[33];
                req[0] = 0x00; // Report ID
                req[1] = CMD_GET_LAYER_COUNT;

                stream.Write(req);
                byte[] resp = stream.Read();

                if (resp != null && resp.Length >= 3 && resp[1] == CMD_GET_LAYER_COUNT)
                {
                    return resp[2]; // Layer count
                }

                return 0;
            }
        }

        public List<ushort> ReadLayerKeycodes(byte layer = 0, byte maxKeys = 19)
        {
            var keycodes = new List<ushort>();
            var device = GetTargetDevice();

            if (device == null || !device.TryOpen(out HidStream stream))
            {
                return keycodes;
            }

            using (stream)
            {
                stream.ReadTimeout = 1000;
                for (byte i = 0; i < maxKeys; i++)
                {
                    byte[] req = new byte[33];
                    req[0] = 0x00; // Report ID
                    req[1] = CMD_GET_KEYCODE;
                    req[2] = layer;
                    req[3] = 0; // Row / High Index
                    req[4] = i; // Column / Low Index (Key Index)

                    stream.Write(req);
                    byte[] resp = stream.Read();

                    if (resp != null && resp.Length >= 7 && resp[1] == CMD_GET_KEYCODE)
                    {
                        // resp[0] = 0x00, resp[1] = 0x04, resp[2] = Layer, resp[3] = Row, resp[4] = Col
                        // resp[5] = Keycode High Byte, resp[6] = Keycode Low Byte
                        ushort keycode = (ushort)((resp[5] << 8) | resp[6]);
                        keycodes.Add(keycode);
                    }
                    else
                    {
                        keycodes.Add(0x0000); // Keycode fallback on read failure
                    }
                }
            }

            return keycodes;
        }

        public bool WriteLayerKeycode(byte layer, byte keyIndex, ushort keycode)
        {
            var device = GetTargetDevice();
            if (device == null || !device.TryOpen(out HidStream stream)) return false;

            using (stream)
            {
                stream.ReadTimeout = 1000;
                byte[] req = new byte[33];
                req[0] = 0x00; // Report ID
                req[1] = CMD_SET_KEYCODE;
                req[2] = layer;
                req[3] = 0;        // Row / High Index
                req[4] = keyIndex; // Column / Low Index (Key Index)
                req[5] = (byte)(keycode >> 8);   // Keycode High Byte
                req[6] = (byte)(keycode & 0xFF); // Keycode Low Byte

                stream.Write(req);
                byte[] resp = stream.Read();

                if (resp == null || resp.Length < 7) return false;

                // Firmware echoes back CMD_SET_KEYCODE (0x05) on successful EEPROM write
                return resp[1] == CMD_SET_KEYCODE && resp[2] == layer && resp[4] == keyIndex;
            }
        }
    }
}
