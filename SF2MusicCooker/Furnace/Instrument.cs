namespace SF2MusicCooker.Furnace
{
    public sealed class Instrument
    {
        public const short PSG = 0x00;
        public const short FM = 0x01;
        public const short DAC = 0x04;

        /// <summary>
        /// The instrument type (PSG, FM, DAC).
        /// </summary>
        public readonly short Type;

        /// <summary>
        /// The instrument user modifiable name.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// The data payload describing this instrument (see Furnace documentation for details).
        /// </summary>
        public readonly byte[] Data;

        /// <summary>
        /// True if no pattern ever references this instrument. This can be used to speed up instrument usage calculation.
        /// </summary>
        public readonly bool Unreferenced;

        public Instrument(short type, string name, byte[] data, bool unreferenced)
        {
            Type = type;
            Name = name;
            Data = data;
            Unreferenced = unreferenced;
        }
    }
}