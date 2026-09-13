using SF2MusicCooker.Furnace;
using System;

namespace SF2MusicCooker
{
    public sealed class NoteCounter
    {
        private readonly int[] _fm;
        private readonly int[] _psg;

        public delegate int Metric(byte note, int count);

        /// <summary>
        /// Reset the note counter.
        /// </summary>
        public void Reset()
        {
            Tools.Fill(_fm, 0);
            Tools.Fill(_psg, 0);
        }

        /// <summary>
        /// Get the number of times a note is used.
        /// </summary>
        public int Get(byte note, bool psg)
        {
            int[] target = psg ? _psg : _fm;
            return target[note];
        }

        /// <summary>
        /// Increment note counters.
        /// </summary>
        public void Add(byte[] notes, bool psg)
        {
            int[] target = psg ? _psg : _fm;
            foreach (byte note in notes) target[note]++;
        }

        /// <summary>
        /// Increment note counters.
        /// </summary>
        public void Add(byte[] notes, byte[] psgNotes)
        {
            foreach (byte note in notes) _fm[note]++;
            foreach (byte psgNote in psgNotes) _psg[psgNote]++;
        }

        /// <summary>
        /// Increment note counters from another counter.
        /// </summary>
        public void Add(NoteCounter other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            for (int i = 0; i < _fm.Length; i++) _fm[i] += other._fm[i];
            for (int i = 0; i < _psg.Length; i++) _psg[i] += other._psg[i];
        }

        /// <summary>
        /// Return notes in descending order specified by the supplied metric.
        /// </summary>
        public byte[] ToSorted(bool psg, Metric metric)
        {
            int[] source = psg ? _psg : _fm;
            int[] values = (int[])source.Clone();
            for (int i = 0; i < values.Length; i++) values[i] = metric((byte)i, source[i]);

            byte[] notes = new byte[source.Length];
            for (int i = 0; i < notes.Length; i++) notes[i] = (byte)i;
            Array.Sort(values, notes);
            Array.Reverse(notes);
            notes = Array.FindAll(notes, note => NoteBible.Clamp(note) == note); // Keep only valid notes
            return notes;
        }

        /// <summary>
        /// Calculate the mean note (or return C-4 if no note is present).
        /// </summary>
        public byte Mean(bool psg)
        {
            int[] source = psg ? _psg : _fm;

            int total = 0;
            foreach (int count in source) total += count;

            if (total > 0)
            {
                int sum = total / 2;
                for (int i = 0; i < source.Length; i++)
                {
                    sum -= source[i];
                    if (sum <= 0) return (byte)i;
                }
            }
            return 0x6C; // C-4
        }

        public NoteCounter()
        {
            _fm = new int[0x100];
            _psg = new int[0x100];
        }

        public NoteCounter(NoteCounter other)
        {
            _fm = (int[])other._fm.Clone();
            _psg = (int[])other._psg.Clone();
        }
    }
}