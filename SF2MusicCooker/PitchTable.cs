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
        private readonly Entry[] _psgNotes;
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
        /// Get PSG tone frequency from its raw register value.
        /// </summary>
        public static int GetPSGFrequency(int value)
        {
            // https://www.vgmpf.com/Wiki/images/7/78/SN76489AN_-_Manual.pdf

            return 3579545 / (32 * value); // Using Z80 NTSC clock rate
        }

        /// <summary>
        /// Get the name of a Cube note.
        /// </summary>
        public string GetCubeNoteName(int cubeNote)
        {
            if (_names != null && _names.TryGetValue(cubeNote, out string name))
                return name;
            else
                return cubeNote.ToString();
        }

        /// <summary>
        /// Create a tuned map for the specified A4 tuning value for YM2612.
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
            return new TunedMap(f2y, GetCubeNoteName);
        }

        /// <summary>
        /// Create a tuned map for the specified A4 tuning value for PSG tone.
        /// </summary>
        public TunedMap CreatePSGTunedMap(int a4tuning)
        {
            // TODO: factorize
            int[] f2t = new int[NoteBible.LENGTH];
            if (_psgNotes.Length > 0)
            {
                for (int i = 0; i < f2t.Length; i++)
                {
                    int frequency = GetFurnaceFrequency(a4tuning, NoteBible.BASE_VALUE + i);
                    Entry entry = Tools.SelectMin(_psgNotes, e => Math.Abs(e.Frequency - frequency));
                    f2t[i] = entry.Note;
                }
            }
            return new TunedMap(f2t, GetCubeNoteName);
        }

        private static Entry[] ReadFrequencies(string path, Func<int, int> freqFn)
        {
            string asm = File.ReadAllText(path);
            int[] values = Tools.GetAllNumericElements(asm, "dw");

            Entry[] entries = new Entry[values.Length];
            for (int i = 0; i < entries.Length; i++) entries[i] = new Entry(i, freqFn(values[i]));
            return entries;
        }

        public PitchTable(string ymFrequenciesPath, string psgFrequenciesPath, string notesNamePath = null)
        {
            _notes = ReadFrequencies(ymFrequenciesPath, GetYMFrequency);
            _psgNotes = ReadFrequencies(psgFrequenciesPath, GetPSGFrequency);
            if (notesNamePath != null) _names = Tools.ReadASMEnumReverseMap(notesNamePath);
        }

        private PitchTable()
        {
            _notes = _psgNotes = new Entry[0];
        }

        /// <summary>
        /// Represents an empty pitch table (it can handle empty Furnace files).
        /// </summary>
        public static readonly PitchTable Empty = new PitchTable();
    }
}