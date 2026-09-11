using System;

namespace SF2MusicCooker
{
    public sealed class RegisterLookup
    {
        private readonly int[] _frequencies;
        private readonly int[] _psgFrequencies;

        /// <summary>
        /// Get the best register value to use to hit the specified frequency value.
        /// </summary>
        public ushort GetYMRegister(int frequency)
        {
            int bestFrequency = Tools.SelectMin(_frequencies, f => Math.Abs(frequency - f));
            return (ushort)Array.IndexOf(_frequencies, bestFrequency);
        }

        /// <summary>
        /// Get the best register value to use to hit the specified frequency value.
        /// </summary>
        public ushort GetPSGRegister(int frequency)
        {
            int bestFrequency = Tools.SelectMin(_frequencies, f => Math.Abs(frequency - f));
            return (ushort)Array.IndexOf(_frequencies, bestFrequency);
        }

        public RegisterLookup()
        {
            // NOTE: if it takes too much time, we could improve this with dicotomic search

            _frequencies = new int[0x10000];
            for (int i = 0; i <= 0xFFFF; i++) _frequencies[i] = PitchTable.GetYMFrequency(i);

            _psgFrequencies = new int[0x10000];
            for (int i = 1; i <= 0xFFFF; i++) _psgFrequencies[i] = PitchTable.GetPSGFrequency(i);
        }
    }
}