using System.Collections.Generic;

namespace SF2MusicCooker
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

        public const int DEFAULT_NOTE = 48 + NoteBible.BASE_VALUE; // C-4

        private readonly int _defaultSample;
        private readonly Dictionary<int, Entry> _note2sample;

        public Entry Read(int note)
        {
            if (_note2sample != null && _note2sample.TryGetValue(note, out Entry entry)) return entry;
            return new Entry(_defaultSample, DEFAULT_NOTE);
        }

        public SampleMap(int defaultSample, Dictionary<int, Entry> note2sample = null)
        {
            _defaultSample = defaultSample;
            _note2sample = note2sample;
        }
    }
}