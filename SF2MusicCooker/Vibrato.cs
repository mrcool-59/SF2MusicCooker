namespace SF2MusicCooker
{
    public static class Vibrato
    {
        /// <summary>
        /// Given the supplied vibrato state, return the vibrato index that is best suited for the job.
        /// </summary>
        public static byte ResolveIndex(VibratoState state)
        {
            return ResolveIndex(state.Shape, state.Speed, state.Depth);
        }

        /// <summary>
        /// Given the supplied vibrato speed and depth, return the vibrato index that is best suited for the job.
        /// </summary>
        public static byte ResolveIndex(byte shape, byte speed, byte depth)
        {
            // Using WizTools logic to select a vibrato index

            // FIXME: we could populate the vibrato data definitions dynamically depending on vibratos used in musics
            // Like we do currently for FM instruments and PCM samples :-)
            // But since it would require huge efforts for low gains, in the meantime, we'll be satisfied with the stock vibrato data

            const int DEFAULT_INDEX = 2;

            switch (shape)
            {
                case 0x06: // Square
                    return 1;
                case 0x00: // Sine
                    if (depth >= 2)
                        return DEFAULT_INDEX;
                    else if (speed >= 5)
                        return 4;
                    else
                        return 5;
                case 0x04: // Ramp up
                    if (speed >= 15)
                        return 14;
                    else if (speed >= 12)
                        return 12;
                    else if (speed >= 8)
                        return 10;
                    else if (speed >= 4)
                        return 8;
                    else
                        return 6;
                case 0x05: // Ramp down
                    if (speed >= 15)
                        return 15;
                    else if (speed >= 12)
                        return 13;
                    else if (speed >= 8)
                        return 11;
                    else if (speed >= 4)
                        return 9;
                    else
                        return 7;
                default: // Unsupported: default to sine
                    return DEFAULT_INDEX;
            }
        }

        /*
        public Vibrato()
        {
            // For now, we use the stock SF2 vibrato table, we don't cook our own
            // Given how much effort it would require for low payoff, I think I won't go much farther here!
        }
        */
    }
}