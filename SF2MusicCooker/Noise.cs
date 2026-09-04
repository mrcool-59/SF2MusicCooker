using SF2MusicCooker.Furnace;

namespace SF2MusicCooker
{
    public static class Noise
    {
        /// <summary>
        /// Furnace note to noise frequency value.
        /// </summary>
        public static byte F2N(byte note, bool useTone3Frequency)
        {
            if (useTone3Frequency)
                return 0b11;
            else if (note >= NoteBible.GetByName("A-4").Value)
                return 0b10;
            else if (note >= NoteBible.GetByName("A-3").Value)
                return 0b01;
            else
                return 0b00; // A-2
        }

        /// <summary>
        /// Get the proper noise value to submit to Cube sound driver.
        /// </summary>
        public static byte Value(byte note, byte mode)
        {
            byte tone3freq = (byte)((mode >> 4) & 0x01);
            byte feedback = (byte)(mode & 0x01);

            byte frequency = F2N(note, tone3freq != 0);
            byte value = (byte)((feedback << 2) | frequency);

            return value;
        }
    }
}