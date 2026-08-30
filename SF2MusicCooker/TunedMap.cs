using SF2MusicCooker.Furnace;
using System;
using System.Collections.Generic;

namespace SF2MusicCooker
{
    public sealed class TunedMap
    {
        private readonly int[] _f2y;
        private readonly Func<int, string> _y2name;
        private readonly List<int> _clamped;
        private readonly int _minNote;
        private readonly int _maxNote;

        /// <summary>
        /// The list of notes that have been clamped because they're not supported on YM side.
        /// </summary>
        public int[] Clamped
        {
            get
            {
                _clamped.Sort();
                return _clamped.ToArray();
            }
        }

        /// <summary>
        /// Find the YM note for a given Furnace 'note'.
        /// </summary>
        public int F2Y(byte note)
        {
            NoteBible.Verify(note);
            CheckClamped(note);
            return _f2y[note - NoteBible.BASE_VALUE];
        }

        /// <summary>
        /// Find the YM note name for a given Furnace 'note'.
        /// </summary>
        public string F2YName(byte note)
        {
            return _y2name(F2Y(note));
        }

        private void CheckClamped(byte note)
        {
            if (note < _minNote || note > _maxNote)
            {
                if (!_clamped.Contains(note))
                    _clamped.Add(note);
            }
        }

        public TunedMap(int[] f2y, Func<int, string> y2name = null)
        {
            if (f2y == null || f2y.Length != NoteBible.LENGTH)
                throw new ArgumentException(nameof(f2y), "must have length " + NoteBible.LENGTH);

            _f2y = f2y;
            _y2name = y2name ?? (x => x.ToString());
            _clamped = new List<int>();

            int max = f2y.Length - 1;
            while (max - 1 >= 0 && f2y[max - 1] == f2y[max]) max--;
            _maxNote = max + NoteBible.BASE_VALUE;

            int min = 0;
            while (min + 1 < f2y.Length && f2y[min + 1] == f2y[min]) min++;
            _minNote = min + NoteBible.BASE_VALUE;
        }
    }
}