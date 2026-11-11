using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace CyberGrip
{
    public static class QmkKeyMap
    {
        private static readonly Dictionary<Key, ushort> KeyToQmkMap = new()
        {
            // Letters
            { Key.A, 0x0004 }, { Key.B, 0x0005 }, { Key.C, 0x0006 }, { Key.D, 0x0007 },
            { Key.E, 0x0008 }, { Key.F, 0x0009 }, { Key.G, 0x000A }, { Key.H, 0x000B },
            { Key.I, 0x000C }, { Key.J, 0x000D }, { Key.K, 0x000E }, { Key.L, 0x000F },
            { Key.M, 0x0010 }, { Key.N, 0x0011 }, { Key.O, 0x0012 }, { Key.P, 0x0013 },
            { Key.Q, 0x0014 }, { Key.R, 0x0015 }, { Key.S, 0x0016 }, { Key.T, 0x0017 },
            { Key.U, 0x0018 }, { Key.V, 0x0019 }, { Key.W, 0x001A }, { Key.X, 0x001B },
            { Key.Y, 0x001C }, { Key.Z, 0x001D },

            // Numbers
            { Key.D1, 0x001E }, { Key.D2, 0x001F }, { Key.D3, 0x0020 }, { Key.D4, 0x0021 },
            { Key.D5, 0x0022 }, { Key.D6, 0x0023 }, { Key.D7, 0x0024 }, { Key.D8, 0x0025 },
            { Key.D9, 0x0026 }, { Key.D0, 0x0027 },

            // Controls & Punctuation
            { Key.Return,      0x0028 }, // KC_ENTER
            { Key.Escape,      0x0029 }, // KC_ESCAPE
            { Key.Back,        0x002A }, // KC_BACKSPACE
            { Key.Tab,         0x002B }, // KC_TAB
            { Key.Space,       0x002C }, // KC_SPACE
            { Key.OemMinus,    0x002D }, // KC_MINUS (-)
            { Key.OemPlus,     0x002E }, // KC_EQUAL (=)
            { Key.OemOpenBrackets,  0x002F }, // KC_LBRC ([)
            { Key.OemCloseBrackets, 0x0030 }, // KC_RBRC (])
            { Key.Oem5,     0x0031 }, // KC_BSLS (\)
            { Key.OemBackslash,     0x0031 }, // KC_BSLS (\)
            { Key.OemSemicolon,     0x0033 }, // KC_SCLN (;)
            { Key.OemQuotes,        0x0034 }, // KC_QUOT (')
            { Key.OemTilde,         0x0035 }, // KC_GRV  (`)
            { Key.OemComma,         0x0036 }, // KC_COMM (,)
            { Key.OemPeriod,        0x0037 }, // KC_DOT  (.)
            { Key.OemQuestion,      0x0038 }, // KC_SLSH (/)

            // Standalone Modifiers
            { Key.LeftShift,   0x00E1 }, { Key.RightShift, 0x00E1 },
            { Key.LeftCtrl,    0x00E0 }, { Key.RightCtrl,  0x00E0 },
            { Key.LeftAlt,     0x00E2 }, { Key.RightAlt,   0x00E2 },
            { Key.LWin,        0x00E3 }, { Key.RWin,       0x00E3 },

            // Function Keys
            { Key.F1, 0x003A }, { Key.F2, 0x003B }, { Key.F3, 0x003C }, { Key.F4, 0x003D },
            { Key.F5, 0x003E }, { Key.F6, 0x003F }, { Key.F7, 0x0040 }, { Key.F8, 0x0041 },
            { Key.F9, 0x0042 }, { Key.F10, 0x0043 }, { Key.F11, 0x0044 }, { Key.F12, 0x0045 },

            // Navigation
            { Key.Insert, 0x0049 }, { Key.Home, 0x004A }, { Key.PageUp, 0x004B },
            { Key.Delete, 0x004C }, { Key.End, 0x004D }, { Key.PageDown, 0x004E },
            { Key.Right,  0x004F }, { Key.Left, 0x0050 }, { Key.Down, 0x0051 }, { Key.Up, 0x0052 }
        };

        public static ushort GetQmkCodeFromWpfKey(Key key)
        {
            return KeyToQmkMap.TryGetValue(key, out ushort qmkCode) ? qmkCode : (ushort)0x0000;
        }

        public static string GetStringFromQmkCode(ushort qmkCode)
        {
            if (qmkCode == 0x0000) return "KC_NO";
            if (qmkCode == 0x0001) return "KC_TRNS";

            // VIA Layers
            if (qmkCode >= 0x5201 && qmkCode <= 0x5203) return $"MO({qmkCode - 0x5200})";
            if (qmkCode >= 0x5251 && qmkCode <= 0x5253) return $"TG({qmkCode - 0x5250})";
            if (qmkCode >= 0x5221 && qmkCode <= 0x5223) return $"TO({qmkCode - 0x5220})";

            // Media & Consumer Keycodes
            string? mediaName = GetMediaKeyName(qmkCode);
            if (mediaName != null) return mediaName;

            // Standalone Modifiers
            if (qmkCode == 0x00E0) return "LCTRL";
            if (qmkCode == 0x00E1) return "LSHIFT";
            if (qmkCode == 0x00E2) return "LALT";
            if (qmkCode == 0x00E3) return "LGUI";

            // Combined Modifiers (Bitfield in High Byte)
            if ((qmkCode & 0xFF00) != 0)
            {
                StringBuilder sb = new StringBuilder();
                if ((qmkCode & 0x0100) != 0) sb.Append("C+");
                if ((qmkCode & 0x0200) != 0) sb.Append("S+");
                if ((qmkCode & 0x0400) != 0) sb.Append("A+");
                if ((qmkCode & 0x0800) != 0) sb.Append("G+");

                byte baseCode = (byte)(qmkCode & 0xFF);
                sb.Append(GetBaseKeyName(baseCode));
                return sb.ToString();
            }

            // Basic Keycodes fallback
            return GetBaseKeyName((byte)(qmkCode & 0xFF));
        }

        private static string GetBaseKeyName(byte baseCode)
        {
            return baseCode switch
            {
                0x04 => "A",
                0x05 => "B",
                0x06 => "C",
                0x07 => "D",
                0x08 => "E",
                0x09 => "F",
                0x0A => "G",
                0x0B => "H",
                0x0C => "I",
                0x0D => "J",
                0x0E => "K",
                0x0F => "L",
                0x10 => "M",
                0x11 => "N",
                0x12 => "O",
                0x13 => "P",
                0x14 => "Q",
                0x15 => "R",
                0x16 => "S",
                0x17 => "T",
                0x18 => "U",
                0x19 => "V",
                0x1A => "W",
                0x1B => "X",
                0x1C => "Y",
                0x1D => "Z",
                0x1E => "1",
                0x1F => "2",
                0x20 => "3",
                0x21 => "4",
                0x22 => "5",
                0x23 => "6",
                0x24 => "7",
                0x25 => "8",
                0x26 => "9",
                0x27 => "0",
                0x28 => "ENTER",
                0x29 => "ESC",
                0x2A => "BSPC",
                0x2B => "TAB",
                0x2C => "SPACE",
                0x2D => "-",
                0x2E => "=",
                0x2F => "[",
                0x30 => "]",
                0x31 => "\\",
                0x33 => ";",
                0x34 => "'",
                0x35 => "`",
                0x36 => ",",
                0x37 => ".",
                0x38 => "/",
                0x3A => "F1",
                0x3B => "F2",
                0x3C => "F3",
                0x3D => "F4",
                0x3E => "F5",
                0x3F => "F6",
                0x40 => "F7",
                0x41 => "F8",
                0x42 => "F9",
                0x43 => "F10",
                0x44 => "F11",
                0x45 => "F12",
                0x49 => "INS",
                0x4A => "HOME",
                0x4B => "PGUP",
                0x4C => "DEL",
                0x4D => "END",
                0x4E => "PGDN",
                0x4F => "RIGHT",
                0x50 => "LEFT",
                0x51 => "DOWN",
                0x52 => "UP",
                _ => $"0x{baseCode:X2}"
            };
        }

        private static string? GetMediaKeyName(ushort keycode)
        {
            return keycode switch
            {
                0x00A9 => "KC_VOLU",
                0x00AA => "KC_VOLD",
                0x00A8 => "KC_MUTE",
                0x00AB => "KC_MNXT",
                0x00AC => "KC_MPRV",
                0x00AD => "KC_MSTP",
                0x00AE => "KC_MPLY",
                0x00B0 => "KC_EJCT",
                0x00B1 => "KC_MAIL",
                0x00B2 => "KC_CALC",
                0x00B3 => "KC_MYCM",
                0x00B4 => "KC_WSCH",
                0x00B5 => "KC_WHOM",
                0x00B6 => "KC_WBAK",
                0x00B7 => "KC_WFWD",
                0x00B8 => "KC_WSTP",
                0x00B9 => "KC_WREF",
                0x00BA => "KC_WFAV",
                0x00BD => "KC_BRIU",
                0x00BE => "KC_BRID",
                _ => null
            };
        }
    }
}
