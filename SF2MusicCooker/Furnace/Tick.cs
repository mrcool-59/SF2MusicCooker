using System;

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

        /// <summary>
        /// Print a warning to the user if note/silence length has reached the length threshold. Return true if warning was actually printed.
        /// </summary>
        public bool PrintLengthWarning(int lengthThreshold)
        {
            string what = null;
            if (SilenceLength >= lengthThreshold)
            {
                what = "silence";
                Console.WriteLine("! Silence triggered at {0} has a hit the maximum allowed length ({1} ticks)", Position, SilenceLength);
            }
            if (NoteLength >= lengthThreshold)
            {
                what = "note";
                Console.WriteLine("! Note triggered at {0} has a hit the maximum allowed length ({1} ticks)", Position, NoteLength);
            }
            if (what != null)
            {
                Console.WriteLine("! This is either a bug in the tool or a deliberate, absurdly long {0} in the Furnace file.", what);
                Console.WriteLine("! Regardless of the cause, the consequence is that the actual {0} length will be capped in the output sheet.", what);
                return true;
            }
            return false;
        }
    }
}