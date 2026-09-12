namespace SF2MusicCooker.Furnace
{
    public readonly struct Loop
    {
        /// <summary>
        /// Start position of the loop, or 'None' if there is no loop.
        /// </summary>
        public readonly Position Start;

        /// <summary>
        /// End position of the loop, or 'None' if there is no loop.
        /// </summary>
        public readonly Position End;

        /// <summary>
        /// Number of ticks that were executed before reaching the loop or ending.
        /// </summary>
        public readonly int Ticks;

        public Loop(Position start, Position end, int ticks)
        {
            Start = start;
            End = end;
            Ticks = ticks;
        }

        /// <summary>
        /// Represents the absence of a loop.
        /// </summary>
        public static Position None => new Position(-1, -1);
    }
}