using System;
using System.Collections.Generic;

namespace SF2MusicCooker.Furnace
{
    public static class NoteBible
    {
        public const int BASE_VALUE = 0x3C;
        public const int LAST_VALUE = 0xB3;
        public const int C4_VALUE = 0x6C;

        public const int LENGTH = LAST_VALUE - BASE_VALUE + 1;

        public readonly struct Entry
        {
            public readonly byte Value;
            public readonly string Name;

            public Entry(byte value, string name)
            {
                Value = value;
                Name = name;
            }

            public override string ToString()
            {
                return Name + " [" + Value + "]";
            }

            public override int GetHashCode()
            {
                return Value;
            }

            public override bool Equals(object obj)
            {
                return obj is Entry entry && entry == this;
            }

            public static bool operator ==(Entry a, Entry b)
            {
                return a.Value == b.Value;
            }

            public static bool operator !=(Entry a, Entry b)
            {
                return a.Value != b.Value;
            }
        }

        private static readonly Entry[] entries;
        private static readonly Dictionary<int, Entry> lookupByValue;
        private static readonly Dictionary<string, Entry> lookupByName;

        /// <summary>
        /// The list of all supported notes.
        /// </summary>
        public static IReadOnlyList<Entry> AllSupportedNotes { get { return entries; } }

        /// <summary>
        /// The first supported note.
        /// </summary>
        public static Entry FirstSupportedNote { get { return entries[0]; } }

        /// <summary>
        /// The last supported note.
        /// </summary>
        public static Entry LastSupportedNote { get { return entries[entries.Length - 1]; } }

        /// <summary>
        /// Given a Furnace note value, return the associated note entry. Throws an exception if the note value is not supported.
        /// </summary>
        public static Entry GetByValue(byte value)
        {
            if (lookupByValue.TryGetValue(value, out Entry entry)) return entry;
            throw new NotSupportedException("Note with value 0x" + Tools.Hex1(value) + " (" + value + ") is invalid");
        }

        /// <summary>
        /// Given a Furnace note name, return the associated note entry. Throws an exception if the name is not recognized.
        /// </summary>
        public static Entry GetByName(string name)
        {
            if (lookupByName.TryGetValue(name, out Entry entry)) return entry;
            throw new NotSupportedException("Note '" + name + "' is invalid, unknown or not in a supported octave");
        }

        /// <summary>
        /// Clamp the note inside valid note range.
        /// </summary>
        public static byte Clamp(byte value)
        {
            if (value < BASE_VALUE)
                return BASE_VALUE;
            else if (value > LAST_VALUE)
                return LAST_VALUE;
            else
                return value;
        }

        /// <summary>
        /// Verify note is between BASE_VALUE and LAST_VALUE.
        /// </summary>
        public static void Verify(int note)
        {
            if (note < BASE_VALUE || note > LAST_VALUE)
                throw new ArgumentOutOfRangeException(nameof(note), "note must be between " + BASE_VALUE + " and " + LAST_VALUE);
        }

        static NoteBible()
        {
            entries = new Entry[120]
            {
                new Entry(0x3C, "C-0"), // BASE_VALUE
                new Entry(0x3D, "C#0"),
                new Entry(0x3E, "D-0"),
                new Entry(0x3F, "D#0"),
                new Entry(0x40, "E-0"),
                new Entry(0x41, "F-0"),
                new Entry(0x42, "F#0"),
                new Entry(0x43, "G-0"),
                new Entry(0x44, "G#0"),
                new Entry(0x45, "A-0"),
                new Entry(0x46, "A#0"),
                new Entry(0x47, "B-0"),

                new Entry(0x48, "C-1"),
                new Entry(0x49, "C#1"),
                new Entry(0x4A, "D-1"),
                new Entry(0x4B, "D#1"),
                new Entry(0x4C, "E-1"),
                new Entry(0x4D, "F-1"),
                new Entry(0x4E, "F#1"),
                new Entry(0x4F, "G-1"),
                new Entry(0x50, "G#1"),
                new Entry(0x51, "A-1"),
                new Entry(0x52, "A#1"),
                new Entry(0x53, "B-1"),

                new Entry(0x54, "C-2"),
                new Entry(0x55, "C#2"),
                new Entry(0x56, "D-2"),
                new Entry(0x57, "D#2"),
                new Entry(0x58, "E-2"),
                new Entry(0x59, "F-2"),
                new Entry(0x5A, "F#2"),
                new Entry(0x5B, "G-2"),
                new Entry(0x5C, "G#2"),
                new Entry(0x5D, "A-2"),
                new Entry(0x5E, "A#2"),
                new Entry(0x5F, "B-2"),

                new Entry(0x60, "C-3"),
                new Entry(0x61, "C#3"),
                new Entry(0x62, "D-3"),
                new Entry(0x63, "D#3"),
                new Entry(0x64, "E-3"),
                new Entry(0x65, "F-3"),
                new Entry(0x66, "F#3"),
                new Entry(0x67, "G-3"),
                new Entry(0x68, "G#3"),
                new Entry(0x69, "A-3"),
                new Entry(0x6A, "A#3"),
                new Entry(0x6B, "B-3"),

                new Entry(0x6C, "C-4"), // C4_VALUE
                new Entry(0x6D, "C#4"),
                new Entry(0x6E, "D-4"),
                new Entry(0x6F, "D#4"),
                new Entry(0x70, "E-4"),
                new Entry(0x71, "F-4"),
                new Entry(0x72, "F#4"),
                new Entry(0x73, "G-4"),
                new Entry(0x74, "G#4"),
                new Entry(0x75, "A-4"),
                new Entry(0x76, "A#4"),
                new Entry(0x77, "B-4"),

                new Entry(0x78, "C-5"),
                new Entry(0x79, "C#5"),
                new Entry(0x7A, "D-5"),
                new Entry(0x7B, "D#5"),
                new Entry(0x7C, "E-5"),
                new Entry(0x7D, "F-5"),
                new Entry(0x7E, "F#5"),
                new Entry(0x7F, "G-5"),
                new Entry(0x80, "G#5"),
                new Entry(0x81, "A-5"),
                new Entry(0x82, "A#5"),
                new Entry(0x83, "B-5"),

                new Entry(0x84, "C-6"),
                new Entry(0x85, "C#6"),
                new Entry(0x86, "D-6"),
                new Entry(0x87, "D#6"),
                new Entry(0x88, "E-6"),
                new Entry(0x89, "F-6"),
                new Entry(0x8A, "F#6"),
                new Entry(0x8B, "G-6"),
                new Entry(0x8C, "G#6"),
                new Entry(0x8D, "A-6"),
                new Entry(0x8E, "A#6"),
                new Entry(0x8F, "B-6"),

                new Entry(0x90, "C-7"),
                new Entry(0x91, "C#7"),
                new Entry(0x92, "D-7"),
                new Entry(0x93, "D#7"),
                new Entry(0x94, "E-7"),
                new Entry(0x95, "F-7"),
                new Entry(0x96, "F#7"),
                new Entry(0x97, "G-7"),
                new Entry(0x98, "G#7"),
                new Entry(0x99, "A-7"),
                new Entry(0x9A, "A#7"),
                new Entry(0x9B, "B-7"),

                new Entry(0x9C, "C-8"),
                new Entry(0x9D, "C#8"),
                new Entry(0x9E, "D-8"),
                new Entry(0x9F, "D#8"),
                new Entry(0xA0, "E-8"),
                new Entry(0xA1, "F-8"),
                new Entry(0xA2, "F#8"),
                new Entry(0xA3, "G-8"),
                new Entry(0xA4, "G#8"),
                new Entry(0xA5, "A-8"),
                new Entry(0xA6, "A#8"),
                new Entry(0xA7, "B-8"),

                new Entry(0xA8, "C-9"),
                new Entry(0xA9, "C#9"),
                new Entry(0xAA, "D-9"),
                new Entry(0xAB, "D#9"),
                new Entry(0xAC, "E-9"),
                new Entry(0xAD, "F-9"),
                new Entry(0xAE, "F#9"),
                new Entry(0xAF, "G-9"),
                new Entry(0xB0, "G#9"),
                new Entry(0xB1, "A-9"),
                new Entry(0xB2, "A#9"),
                new Entry(0xB3, "B-9"), // LAST_VALUE
            };

            lookupByValue = new Dictionary<int, Entry>(entries.Length);
            lookupByName = new Dictionary<string, Entry>(entries.Length);

            foreach (Entry entry in entries)
            {
                lookupByValue[entry.Value] = entry;
                lookupByName[entry.Name] = entry;
            }
        }
    }
}
