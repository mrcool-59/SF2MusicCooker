using System;

namespace SF2MusicCooker
{
    public sealed class RegisterValueLookup
    {
        private readonly int[] _frequencies;

        /// <summary>
        /// Get the best register value to use to hit the specified frequency.
        /// </summary>
        public ushort Frequency2Value(int frequency)
        {
            int bestFrequency = Tools.SelectMin(_frequencies, f => Math.Abs(frequency - f));
            return (ushort)Array.IndexOf(_frequencies, bestFrequency);
        }

        public RegisterValueLookup(bool psg)
        {
            // NOTE: if it takes too much time, we could improve this with dicotomic search

            _frequencies = new int[0x10000];

            if (psg)
            {
                for (int i = 1; i <= 0xFFFF; i++) _frequencies[i] = PitchTable.GetPSGFrequency(i);
            }
            else
            {
                for (int i = 0; i <= 0xFFFF; i++) _frequencies[i] = PitchTable.GetYMFrequency(i);
            }
        }
    }
}