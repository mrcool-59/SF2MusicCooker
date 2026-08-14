using System;

namespace SF2MusicCooker
{
    public sealed class Volume
    {
        private readonly bool _useTable;
        private readonly Remapper _remapper;

        /// <summary>
        /// A function that adjusts a given YM volume level and return the new desired YM volume level.
        /// </summary>
        public delegate int Remapper(int ymVolume);

        /// <summary>
        /// Convert YM volume to the closest equivalent Cube volume.
        /// </summary>
        public byte Y2C(byte ymVolume)
        {
            ymVolume = (byte)Math.Max(0, Math.Min(0x7F, _remapper(ymVolume)));
            if (_useTable)
                return yahama2cube[ymVolume];
            else
                return (byte)(ymVolume >> 3); // Simpler but subtly different
        }

        public Volume(bool useTable = true, Remapper remapper = null)
        {
            _useTable = useTable; // If not using table it means the volume will be linearly translated to Cube levels which may produce a subtly different result
            _remapper = remapper ?? (v => v);
        }

        /// <summary>
        /// Build a Volume object from a list of predefined strategies (default, linear, quieter).
        /// </summary>
        public static Volume FromStrategy(string strategy)
        {
            switch (strategy ?? "default")
            {
                case "quieter":
                    return new Volume(true, v => v * 4 / 5);
                case "linear":
                    return new Volume(false, null);
                case "default":
                    return new Volume(true, null);
                default:
                    throw new NotSupportedException("Unknown volume strategy: " + strategy);
            }
        }

        #region Data taken from WizCube

        private static readonly byte[] yahama2cube;

        static Volume()
        {
            byte[] volumes = new byte[16]
            {
                // Value put in YM volume register for each Cube level value (in backwards order I suppose)
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

            yahama2cube = new byte[128];

            byte index = 0;
            byte current = 0x7F;

            while (index < volumes.Length)
            {
                while (volumes[index] <= current)
                {
                    yahama2cube[current] = index;
                    current--;
                }

                index++;
            }

            while (current != 0xFF)
            {
                yahama2cube[current] = 0xF;
                current--;
            }

            Array.Reverse(yahama2cube);
        }

        #endregion
    }
}