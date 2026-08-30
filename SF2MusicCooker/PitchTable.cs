using SF2MusicCooker.Furnace;
using System;
using System.Collections.Generic;
using System.IO;

namespace SF2MusicCooker
{
    public sealed class PitchTable
    {
        private readonly struct Entry
        {
            public readonly int Note;
            public readonly int Frequency;

            public Entry(int note, int frequency)
            {
                Note = note;
                Frequency = frequency;
            }
        }

        private readonly Entry[] _notes;
        private readonly Dictionary<int, string> _names;

        /// <summary>
        /// Get Furnace target frequency of a note.
        /// </summary>
        public static int GetFurnaceFrequency(int a4tuning, int note)
        {
            const int OFFSET = 7; // 3 was the original value I attempted when reading Furnace source code but I guess something escaped me #D
            return ShiftFrequency(a4tuning, note - NoteBible.BASE_VALUE + OFFSET);
        }

        /// <summary>
        /// Get shifted frequency of a note shift.
        /// </summary>
        public static int ShiftFrequency(int frequency, int shift, float coeff = 1f)
        {
            return (int)Math.Round(frequency * coeff * Math.Pow(2.0, shift / 12f));
        }

        /// <summary>
        /// Get YM2612 frequency from its raw register value.
        /// </summary>
        public static int GetYMFrequency(int value)
        {
            // https://plutiedev.com/ym2612-registers#reg-A0

            int block = (value & 0b0011100000000000) >> 11;
            int freq = value & 0b0000011111111111;

            return freq << block;
        }

        /// <summary>
        /// Get the name of a YM note.
        /// </summary>
        public string GetYMNoteName(int ymNote)
        {
            if (_names != null && _names.TryGetValue(ymNote, out string name))
                return name;
            else
                return ymNote.ToString();
        }

        /// <summary>
        /// Get the frequency of a YM note.
        /// </summary>
        public int GetYMNoteFrequency(int ymNote)
        {
            return _notes[ymNote].Frequency;
        }

        /// <summary>
        /// Create a tuned map for the specified A4 tuning value.
        /// </summary>
        public TunedMap CreateTunedMap(int a4tuning)
        {
            int[] f2y = new int[NoteBible.LENGTH];
            if (_notes.Length > 0)
            {
                for (int i = 0; i < f2y.Length; i++)
                {
                    const int OFFSET = 24;
                    int frequency = GetFurnaceFrequency(a4tuning, NoteBible.BASE_VALUE + i);
                    Entry entry = Tools.SelectMin(_notes, e => Math.Abs(e.Frequency - frequency));
                    f2y[i] = entry.Note + OFFSET;
                }
            }
            return new TunedMap(f2y, GetYMNoteName);
        }

        public PitchTable(string ymFrequenciesPath, string notesNamePath = null)
        {
            string asm = File.ReadAllText(ymFrequenciesPath);
            int[] values = Tools.GetAllNumericElements(asm, "dw");

            _notes = new Entry[values.Length];
            for (int i = 0; i < _notes.Length; i++) _notes[i] = new Entry(i, GetYMFrequency(values[i]));

            if (notesNamePath != null) _names = Tools.ReadASMEnumReverseMap(notesNamePath);
        }

        private PitchTable()
        {
            _notes = new Entry[0];
        }

        /// <summary>
        /// Represents an empty pitch table (it can handle empty Furnace files).
        /// </summary>
        public static readonly PitchTable Empty = new PitchTable();
    }
}