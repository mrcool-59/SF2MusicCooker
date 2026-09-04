using SF2MusicCooker.Furnace;
using System;
using System.Collections.Generic;

namespace SF2MusicCooker
{
    public sealed class TunedMap
    {
        private readonly int[] _f2c;
        private readonly Func<int, string> _c2n;
        private readonly List<byte> _clamped;
        private readonly int _minNote;
        private readonly int _maxNote;

        /// <summary>
        /// The list of notes that have been clamped because they're not supported on Cube side.
        /// </summary>
        public byte[] Clamped
        {
            get
            {
                _clamped.Sort();
                return _clamped.ToArray();
            }
        }

        /// <summary>
        /// Find the Cube note for a given Furnace 'note'.
        /// </summary>
        public int F2C(byte note)
        {
            NoteBible.Verify(note);
            CheckClamped(note);
            return _f2c[note - NoteBible.BASE_VALUE];
        }

        /// <summary>
        /// Find the Cube note name for a given Furnace 'note'.
        /// </summary>
        public string F2CName(byte note)
        {
            return _c2n(F2C(note));
        }

        private void CheckClamped(byte note)
        {
            if (note < _minNote || note > _maxNote)
            {
                if (!_clamped.Contains(note))
                    _clamped.Add(note);
            }
        }

        public TunedMap(int[] f2c, Func<int, string> c2n = null)
        {
            if (f2c == null || f2c.Length != NoteBible.LENGTH)
                throw new ArgumentException(nameof(f2c), "must have length " + NoteBible.LENGTH);

            _f2c = f2c;
            _c2n = c2n ?? (x => x.ToString());
            _clamped = new List<byte>();

            int max = f2c.Length - 1;
            while (max - 1 >= 0 && f2c[max - 1] == f2c[max]) max--;
            _maxNote = max + NoteBible.BASE_VALUE;

            int min = 0;
            while (min + 1 < f2c.Length && f2c[min + 1] == f2c[min]) min++;
            _minNote = min + NoteBible.BASE_VALUE;
        }
    }
}