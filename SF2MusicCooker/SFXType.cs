namespace SF2MusicCooker
{
    public enum SFXType
    {
        /// <summary>
        /// Type 1 SFX that may use channel 9 and 10 (PSG square 3 and Noise).
        /// </summary>
        Type1_PSG_Square3_Noise = 1,

        /// <summary>
        /// Type 2 SFX that may use YM channels 4, 5, 6 with channel 6 always in DAC mode.
        /// </summary>
        Type2_YM_Ch4_Ch5_Ch6DAC = 2
    }
}