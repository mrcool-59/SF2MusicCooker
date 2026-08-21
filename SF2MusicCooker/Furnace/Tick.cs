namespace SF2MusicCooker.Furnace
{
    public readonly struct Tick
    {
        /// <summary>
        /// Current position in the playback.
        /// </summary>
        public readonly Position Position;

        /// <summary>
        /// Next position in the playback. Can vary wildly depending on 'go to pattern' and similar effects.
        /// </summary>
        public readonly Position NextPosition;

        /// <summary>
        /// The currently playing cell for the active channel.
        /// </summary>
        public readonly PatternCell ActiveChannelCell;

        /// <summary>
        /// Indicate the length of the new note before release (0 if no note), this is *always* equal to or lower than note length.
        /// </summary>
        public readonly int NoteRelease;

        /// <summary>
        /// Indicate the length of the new note before stopping (0 if no note). Cannot be higher than 'maxPredictLength' specified in the Run method call.
        /// </summary>
        public readonly int NoteLength;

        /// <summary>
        /// Indicate the length of the silence (i.e: note OFF) before the next note (0 if no silence). Cannot be higher than 'maxPredictLength' specified in the Run method call.
        /// </summary>
        public readonly int SilenceLength;

        public Tick(Position position, Position nextPosition, PatternCell activeChannelCell, int noteRelease, int noteLength, int silenceLength)
        {
            Position = position;
            NextPosition = nextPosition;
            ActiveChannelCell = activeChannelCell;
            NoteRelease = noteRelease;
            NoteLength = noteLength;
            SilenceLength = silenceLength;
        }
    }
}