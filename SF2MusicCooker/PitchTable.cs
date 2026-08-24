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
        public static int GetFurnaceFrequency(int a4tuning, byte note)
        {
            const int OFFSET = 7; // 3 was the original value I attempted when reading Furnace source code but I guess something escaped me #D
            return (int)Math.Round(a4tuning * Math.Pow(2.0, (note - NoteBible.BASE_VALUE + OFFSET) / 12f));
        }

        /// <summary>
        /// Get shifted frequency of a note shift.
        /// </summary>
        public static int ShiftFrequency(int frequency, int shift)
        {
            return (int)Math.Round(frequency * Math.Pow(2.0, shift / 12f));
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
        /// Find the best available YM note for a given target 'frequency'. Also return the difference between target and actual frequency.
        /// </summary>
        public int FindBestYMNote(int frequency, out int difference)
        {
            Entry entry = Tools.SelectMin(_notes, e => Math.Abs(e.Frequency - frequency));
            difference = frequency - entry.Frequency;
            return entry.Note;
        }

        /// <summary>
        /// Find the best available YM note for a given Furnace 'note'. Also return the difference between target and actual frequency.
        /// </summary>
        public int MapF2YNote(byte note, int a4tuning, out int difference)
        {
            int frequency = GetFurnaceFrequency(a4tuning, note);
            return FindBestYMNote(frequency, out difference);
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