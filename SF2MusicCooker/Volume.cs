using System;

namespace SF2MusicCooker
{
    public sealed class Volume
    {
        public enum Strategy
        {
            Truncate,
            Nearest,
            Linear
        }

        private readonly byte[] _ym2cube;
        private readonly float _volume;

        /// <summary>
        /// Convert YM volume to the closest equivalent Cube volume.
        /// </summary>
        public byte Y2C(byte ymVolume)
        {
            ymVolume = (byte)Math.Max(0, Math.Min(0x7F, (int)Math.Round(ymVolume * _volume)));
            if (_ym2cube != null)
                return _ym2cube[ymVolume];
            else
                return (byte)(ymVolume >> 3); // Simpler but subtly different
        }

        /// <summary>
        /// Convert Furnace PSG volume to Cube volume (this is a no-op unless a master volume is provided).
        /// </summary>
        public byte PSG(byte psgVolume)
        {
            return (byte)Math.Min(0x0F, Math.Round(psgVolume * _volume));
        }

        public Volume(Strategy strategy = Strategy.Nearest, float volume = 1f)
        {
            if (strategy == Strategy.Linear)
                _ym2cube = null;
            else
                _ym2cube = MakeTable(strategy != Strategy.Nearest);
            _volume = (float)Math.Sqrt(volume);
        }

        public static Strategy ParseStrategy(string x)
        {
            if ("linear".Equals(x, StringComparison.OrdinalIgnoreCase))
                return Strategy.Linear;
            else if ("nearest".Equals(x, StringComparison.OrdinalIgnoreCase))
                return Strategy.Nearest;
            else
                return Strategy.Truncate;
        }

        private static byte[] GetLevels()
        {
            // Data taken from WizCube
            return new byte[16]
            {
                0x70,
                0x60,
                0x50,
                0x40,
                0x38,
                0x30,
                0x2A,
                0x26,
                0x20,
                0x1C,
                0x18,
                0x14,
                0x10,
                0x0B,
                0x08,
                0x04,
            };
        }

        private static byte[] MakeTable(bool truncate)
        {
            byte[] volumes = GetLevels(); // Value put in YM volume register for each Cube level value (in backwards order)
            byte[] ym2cube = new byte[0x80];

            if (truncate)
            {
                byte index = 0;
                byte current = 0x7F;

                while (index < volumes.Length)
                {
                    while (volumes[index] <= current)
                    {
                        ym2cube[current] = index;
                        current--;
                    }

                    index++;
                }

                byte last = (byte)(volumes.Length - 1);
                while (current != 0xFF)
                {
                    ym2cube[current] = last;
                    current--;
                }
            }
            else
            {
                for (int i = 0; i < ym2cube.Length; i++)
                {
                    byte cubeVolume = Tools.SelectMin(volumes, v => Math.Abs(v - i));
                    ym2cube[i] = (byte)Array.IndexOf(volumes, cubeVolume);
                }
            }

            Array.Reverse(ym2cube);

            return ym2cube;
        }
    }
}