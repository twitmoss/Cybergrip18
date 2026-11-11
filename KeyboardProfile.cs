using System;
using System.Collections.Generic;

namespace CyberGrip
{
    public class KeyboardProfile
    {
        public string ProfileName { get; set; } = "Default Profile";
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public ushort VendorId { get; set; } = 0xFEED;
        public ushort ProductId { get; set; } = 0x5A21;
        public int TotalKeys { get; set; } = 19;

        // Null means the profile predates joystick inversion settings or was
        // exported from firmware that does not expose them.
        public bool? InvertX { get; set; }
        public bool? InvertY { get; set; }

        // Per-layer inversion settings. The top-level properties above remain
        // for compatibility with existing profile files and older app builds.
        public Dictionary<byte, JoystickInversionProfile>? LayerJoystickInversions { get; set; }

        // Dictionary mapping Layer Index (0, 1, 2, 3...) to a List of 19 keycodes
        public Dictionary<byte, List<ushort>> Layers { get; set; } = new();
    }

    public class JoystickInversionProfile
    {
        public bool InvertX { get; set; }
        public bool InvertY { get; set; }
    }
}
