using SF2MusicCooker.Furnace;
using System;
using System.Collections.Generic;
using System.IO;

namespace SF2MusicCooker
{
    public sealed class PitchTable
    {
        // Natural
        /*
        private static readonly float[] octave4freqs = new float[]
        {
            264.00f, // C
            275.00f,
            297.00f,
            316.80f,
            330.00f,
            352.00f,
            371.25f,
            396.00f,
            412.50f,
            440.00f, // A
            475.20f,
            495.00f, // B
        };
        */

        // C = 264
        /*
        private static readonly float[] octave4freqs = new float[]
        {
            264.00f, // C
            279.70f,
            296.33f,
            313.95f,
            332.62f,
            352.40f,
            373.35f,
            395.55f,
            419.07f,
            443.99f, // A
            470.39f,
            498.37f, // B
        };
        */

        // A = 440 [Current implementation, +1 octave = double frequency, -1 octave = halve frequency]
        /*
        private static readonly float[] octave4freqs = new float[]
        {
            261.63f, // C
            277.18f,
            293.66f,
            311.13f,
            329.63f,
            349.23f,
            369.99f,
            392.00f,
            415.30f,
            440f,    // A
            466.16f,
            493.88f, // B
        };
        */

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
            return ShiftFrequency(a4tuning, note);
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

            return 3570000 / (32 * value); // Using Z80 NTSC clock rate
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

        private TunedMap CreateTunedMap(Entry[] notes, int a4tuning, int noteShift, int offset)
        {
            int[] f2c = new int[NoteBible.LENGTH];
            if (notes.Length > 0)
            {
                for (int i = 0; i < f2c.Length; i++)
                {
                    int frequency = GetFurnaceFrequency(a4tuning, i + noteShift);
                    Entry entry = Tools.SelectMin(notes, e => Math.Abs(e.Frequency - frequency));
                    f2c[i] = entry.Note + offset;
                }
            }
            return new TunedMap(f2c, GetCubeNoteName);
        }

        /// <summary>
        /// Create a tuned map for the specified A4 tuning value for YM2612.
        /// </summary>
        public TunedMap CreateTunedMap(int a4tuning)
        {
            // 3 was the original note shift I attempted when reading Furnace source code but I guess something escaped me #D
            // 24 is the hardcoded offset in macros.asm
            return CreateTunedMap(_notes, a4tuning, 7, 24);
        }

        /// <summary>
        /// Create a tuned map for the specified A4 tuning value for PSG tone.
        /// </summary>
        public TunedMap CreatePSGTunedMap(int a4tuning)
        {
            // I didn't investigate why there is -1 octave (-12)
            return CreateTunedMap(_psgNotes, a4tuning, -12, 0);
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