using SF2MusicCooker.Furnace;
using System;
using System.Collections.Generic;

namespace SF2MusicCooker
{
    public sealed class TunedMap
    {
        private readonly byte[] _f2c;
        private readonly Func<byte, string> _c2n;
        private readonly List<byte> _clamped;

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
        /// Verify if the given Furnace 'note' is supported. If it's not, it will be clamped.
        /// </summary>
        public bool IsSupported(byte note)
        {
            return (_f2c[note] & 0x80) == 0;
        }

        /// <summary>
        /// Find the Cube note for a given Furnace 'note'.
        /// </summary>
        public byte F2C(byte note)
        {
            NoteBible.Verify(note);
            CheckClamped(note);
            return (byte)(_f2c[note - NoteBible.BASE_VALUE] & 0x7F); // Ignore bit 7 (supported flag)
        }

        /// <summary>
        /// Find the Cube notes for given Furnace 'notes'.
        /// </summary>
        public byte[] F2C(byte[] notes)
        {
            byte[] cubeNotes = new byte[notes.Length];
            for (int i = 0; i < cubeNotes.Length; i++) cubeNotes[i] = F2C(notes[i]);
            return cubeNotes;
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
            if (!IsSupported(note))
            {
                if (!_clamped.Contains(note))
                    _clamped.Add(note);
            }
        }

        public TunedMap(byte[] f2c, Func<byte, string> c2n = null)
        {
            if (f2c == null || f2c.Length != NoteBible.LENGTH)
                throw new ArgumentException(nameof(f2c), "must have length " + NoteBible.LENGTH);

            _f2c = f2c;
            _c2n = c2n ?? (x => x.ToString());
            _clamped = new List<byte>();
        }
    }
}