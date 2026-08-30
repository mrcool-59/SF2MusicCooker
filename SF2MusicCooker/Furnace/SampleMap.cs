using System;

namespace SF2MusicCooker.Furnace
{
    public sealed class SampleMap
    {
        public readonly struct Entry
        {
            /// <summary>
            /// The index of the sample to play.
            /// </summary>
            public readonly int Sample;

            /// <summary>
            /// The note to play for the sample (i.e: alter the sample playback pitch).
            /// </summary>
            public readonly int Note;

            /// <summary>
            /// True if the table entry is invalid.
            /// </summary>
            public bool Invalid => Sample < 0;

            public Entry(int sample, int note)
            {
                Sample = sample;
                Note = note;
            }

            public override string ToString()
            {
                return Sample + ", " + Note;
            }

            public override int GetHashCode()
            {
                return Sample + Note * 373;
            }

            public override bool Equals(object obj)
            {
                return obj is Entry entry && entry == this;
            }

            public static bool operator ==(Entry a, Entry b)
            {
                return a.Sample == b.Sample && a.Note == b.Note;
            }

            public static bool operator !=(Entry a, Entry b)
            {
                return a.Sample != b.Sample || a.Note != b.Note;
            }
        }

        private readonly int _defaultSample;
        private readonly Entry[] _note2sample;

        /// <summary>
        /// Read the sample index and note they should play at for a given input note.
        /// </summary>
        public Entry Read(int note)
        {
            NoteBible.Verify(note);

            if (_note2sample != null)
                return _note2sample[note - NoteBible.BASE_VALUE];
            else
                return new Entry(_defaultSample, NoteBible.C4_VALUE);
        }

        /// <summary>
        /// Get all entries in the sample map that don't play their samples at normal rate (C-4).
        /// </summary>
        public Entry[] PitchShiftedEntries
        {
            get
            {
                if (_note2sample != null)
                    return Array.FindAll(_note2sample, entry => !entry.Invalid && entry.Note != NoteBible.C4_VALUE);
                else
                    return Array.Empty<Entry>();
            }
        }

        public SampleMap(int defaultSample, Entry[] note2sample = null)
        {
            if (note2sample != null && note2sample.Length != NoteBible.LENGTH)
                throw new ArgumentException(nameof(note2sample), "must have length " + NoteBible.LENGTH);

            _defaultSample = defaultSample;
            _note2sample = note2sample;
        }
    }
}