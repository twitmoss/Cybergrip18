using System;
using System.Collections.Generic;

namespace CyberGrip
{
    public static class KeycodeDecoder
    {
        /// <summary>
        /// Decodes a 16-bit QMK keycode into a user-friendly display string.
        /// </summary>
        public static string GetFriendlyKeyName(ushort keycode)
        {
            if (keycode == 0x0000) return "KC_NO";
            if (keycode == 0x0001) return "KC_TRNS";

            // 1. VIA / QMK Layer Commands
            if (keycode >= 0x5200 && keycode <= 0x521F) return $"MO({keycode - 0x5200})";
            if (keycode >= 0x5220 && keycode <= 0x523F) return $"TO({keycode - 0x5220})";
            if (keycode >= 0x5240 && keycode <= 0x525F) return $"TG({keycode - 0x5240})";
            if (keycode >= 0x5260 && keycode <= 0x527F) return $"TT({keycode - 0x5260})";
            if (keycode >= 0x5280 && keycode <= 0x529F) return $"OSL({keycode - 0x5280})";

            // 2. QMK Layer-Tap (LT): 0x4000 - 0x4FFF
            if (keycode >= 0x4000 && keycode <= 0x4FFF)
            {
                byte layer = (byte)((keycode >> 8) & 0x0F);
                byte baseCode = (byte)(keycode & 0xFF);
                return $"LT({layer}, {GetBaseKeyName(baseCode)})";
            }

            // 3. QMK Mod-Tap (MT): 0x2000 - 0x3FFF
            if (keycode >= 0x2000 && keycode <= 0x3FFF)
            {
                byte modMask = (byte)((keycode >> 8) & 0x1F);
                byte baseCode = (byte)(keycode & 0xFF);
                string modStr = GetModMaskName(modMask);
                return $"MT({modStr}, {GetBaseKeyName(baseCode)})";
            }

            // 4. QMK / VIA Standard Media & Consumer Keycodes (Aligned with VIAL)
            string? mediaName = GetMediaKeyName(keycode);
            if (mediaName != null) return mediaName;

            // 5. QMK Combined Modifiers (Bitfield in High Byte: 0x0100 = Ctrl, 0x0200 = Shift, 0x0400 = Alt, 0x0800 = Win)
            if ((keycode & 0xFF00) != 0)
            {
                byte modBits = (byte)(keycode >> 8);
                byte baseCode = (byte)(keycode & 0xFF);

                // Standard QMK Left Shift shortcut alias (0x02xx mask)
                if (modBits == 0x02)
                {
                    string? shifted = GetShiftedSymbol(baseCode);
                    if (shifted != null) return shifted;
                }

                var mods = new List<string>();
                if ((modBits & 0x01) != 0 || (modBits & 0x10) != 0) mods.Add("Ctrl");
                if ((modBits & 0x02) != 0 || (modBits & 0x20) != 0) mods.Add("Shift");
                if ((modBits & 0x04) != 0 || (modBits & 0x40) != 0) mods.Add("Alt");
                if ((modBits & 0x08) != 0 || (modBits & 0x80) != 0) mods.Add("Win");

                if (mods.Count > 0)
                {
                    return $"{string.Join("+", mods)}+{GetBaseKeyName(baseCode)}";
                }
            }

            // 6. Basic Keycodes
            return GetBaseKeyName((byte)(keycode & 0xFF));
        }

        private static string GetModMaskName(byte modMask)
        {
            var mods = new List<string>();
            if ((modMask & 0x01) != 0) mods.Add("Ctrl");
            if ((modMask & 0x02) != 0) mods.Add("Shift");
            if ((modMask & 0x04) != 0) mods.Add("Alt");
            if ((modMask & 0x08) != 0) mods.Add("Win");
            return mods.Count > 0 ? string.Join("|", mods) : "MOD";
        }

        private static string? GetShiftedSymbol(byte baseCode)
        {
            return baseCode switch
            {
                0x1E => "!",  // Shift + 1
                0x1F => "@",  // Shift + 2
                0x20 => "#",  // Shift + 3
                0x21 => "$",  // Shift + 4
                0x22 => "%",  // Shift + 5
                0x23 => "^",  // Shift + 6
                0x24 => "&",  // Shift + 7
                0x25 => "*",  // Shift + 8
                0x26 => "(",  // Shift + 9
                0x27 => ")",  // Shift + 0
                0x2D => "_",  // Shift + -
                0x2E => "+",  // Shift + =
                0x2F => "{",  // Shift + [
                0x30 => "}",  // Shift + ]
                0x31 => "|",  // Shift + \
                0x33 => ":",  // Shift + ;
                0x34 => "\"", // Shift + '
                0x35 => "~",  // Shift + `
                0x36 => "<",  // Shift + ,
                0x37 => ">",  // Shift + .
                0x38 => "?",  // Shift + /
                _ => null
            };
        }


        private static string? GetMediaKeyName(ushort keycode)
        {
            // VIAL / QMK Consumer Keycode mappings (Aligned with VIAL standard page specs)
            return keycode switch
            {
                0x00A9 => "Vol +",
                0x00AA => "Vol -",
                0x00A8 => "Mute",
                0x00AB => "Next Track",
                0x00AC => "Prev Track",
                0x00AD => "Stop",
                0x00AE => "Play/Pause",
                0x00B0 => "Eject",
                0x00B1 => "Mail",
                0x00B2 => "Calculator",
                0x00B3 => "My Computer",
                0x00B4 => "WWW Search",
                0x00B5 => "WWW Home",
                0x00B6 => "WWW Back",
                0x00B7 => "WWW Forward",
                0x00B8 => "WWW Stop",
                0x00B9 => "WWW Refresh",
                0x00BA => "WWW Favorites",
                0x00BD => "Brightness +",
                0x00BE => "Brightness -",
                _ => null
            };
        }

        public static string GetBaseKeyName(byte key)
        {
            return key switch
            {
                // Letters
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

                // Numbers
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

                // Controls & Punctuation
                0x28 => "Enter",
                0x29 => "Esc",
                0x2A => "Backspace",
                0x2B => "Tab",
                0x2C => "Space",
                0x2D => "-",
                0x2E => "=",
                0x2F => "[",
                0x30 => "]",
                0x31 => "\\",
                0x32 => "#",
                0x33 => ";",
                0x34 => "'",
                0x35 => "`",
                0x36 => ",",
                0x37 => ".",
                0x38 => "/",
                0x39 => "Caps Lock",

                // Function Keys
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

                // Navigation
                0x46 => "Print Screen",
                0x47 => "Scroll Lock",
                0x48 => "Pause",
                0x49 => "Insert",
                0x4A => "Home",
                0x4B => "PageUp",
                0x4C => "Delete",
                0x4D => "End",
                0x4E => "PageDown",
                0x4F => "Right",
                0x50 => "Left",
                0x51 => "Down",
                0x52 => "Up",

                // Keypad
                0x53 => "NumLock",
                0x54 => "KP /",
                0x55 => "KP *",
                0x56 => "KP -",
                0x57 => "KP +",
                0x58 => "KP Enter",
                0x59 => "KP 1",
                0x5A => "KP 2",
                0x5B => "KP 3",
                0x5C => "KP 4",
                0x5D => "KP 5",
                0x5E => "KP 6",
                0x5F => "KP 7",
                0x60 => "KP 8",
                0x61 => "KP 9",
                0x62 => "KP 0",
                0x63 => "KP .",

                // Standalone Modifiers
                0xE0 => "Ctrl",
                0xE1 => "Shift",
                0xE2 => "Alt",
                0xE3 => "Win",
                0xE4 => "RCtrl",
                0xE5 => "RShift",
                0xE6 => "RAlt",
                0xE7 => "RWin",

                _ => $"0x{key:X2}"
            };
        }
    }
}
