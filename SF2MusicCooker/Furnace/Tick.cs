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

        /// <summary>
        /// Current vibrato state, this is only useful when a new note is about to start (i.e: NoteLength > 0).
        /// </summary>
        public readonly VibratoState Vibrato;

        public Tick(Position position, Position nextPosition, PatternCell activeChannelCell, int noteRelease, int noteLength, int silenceLength, VibratoState vibrato)
        {
            Position = position;
            NextPosition = nextPosition;
            ActiveChannelCell = activeChannelCell;
            NoteRelease = noteRelease;
            NoteLength = noteLength;
            SilenceLength = silenceLength;
            Vibrato = vibrato;
        }

        /// <summary>
        /// Get a friendly representation of a tick.
        /// </summary>
        public string Dump()
        {
            string suffix = string.Empty;
            string next = string.Empty;

            if (SilenceLength > 0)
            {
                suffix = "  silence length = " + SilenceLength;
            }
            else if (NoteLength > 0)
            {
                if (NoteRelease == NoteLength)
                {
                    suffix = "  note length = " + NoteLength;
                }
                else
                {
                    suffix = "  note release in = " + NoteRelease + ", note length = " + NoteLength;
                }

                if (Vibrato.Active) suffix += ", vibrato = " + Vibrato;
            }

            if (NextPosition <= Position)
            {
                next = "  [go to " + NextPosition + "]";
            }

            return Position + "  " + ActiveChannelCell + suffix + next;
        }
    }
}