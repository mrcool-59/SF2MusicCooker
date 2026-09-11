using System;
using System.Collections.Generic;

namespace SF2MusicCooker
{
    public sealed class ExtendedNotes
    {
        // TODO: finish / use this

        private readonly Dictionary<byte, byte> _extendedF2C;
        private readonly Dictionary<byte, ushort> _extendedCubeNotes;
        private readonly FurnaceNoteToRegisterValue _resolver;

        public delegate ushort FurnaceNoteToRegisterValue(int a4tuning, byte note);

        private byte GetFreeExtendNote()
        {
            foreach (var pair in _extendedCubeNotes)
            {
                if (pair.Value == 0)
                    return pair.Key;
            }

            return 0xFF;
        }

        /// <summary>
        /// Try to get or add an extended note.
        /// </summary>
        public bool TryAddOrGet(int a4tuning, byte note, out byte cubeNote)
        {
            if (_extendedF2C.TryGetValue(note, out cubeNote)) return true;

            cubeNote = GetFreeExtendNote();

            if (cubeNote != 0xFF)
            {
                _extendedF2C.Add(note, cubeNote);
                _extendedCubeNotes[cubeNote] = _resolver(a4tuning, note);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get the map of extended notes that are effectively being used.
        /// </summary>
        internal Dictionary<byte, ushort> GetExtendedNotes()
        {
            Dictionary<byte, ushort> map = new Dictionary<byte, ushort>(_extendedCubeNotes.Count);
            foreach (var pair in _extendedCubeNotes)
            {
                if (pair.Value != 0)
                    map.Add(pair.Key, pair.Value);
            }
            return map;
        }

        public ExtendedNotes(byte[] cubeNotes, FurnaceNoteToRegisterValue resolver)
        {
            if (cubeNotes == null)
                throw new ArgumentNullException(nameof(cubeNotes));

            if (resolver == null)
                throw new ArgumentNullException(nameof(resolver));

            _extendedF2C = new Dictionary<byte, byte>();
            _extendedCubeNotes = new Dictionary<byte, ushort>();
            _resolver = resolver;

            foreach (byte cubeNote in cubeNotes)
            {
                _extendedCubeNotes.Add(cubeNote, 0); // Extended notes start in "unused/available for use" state
            }
        }

        /// <summary>
        /// Extended notes object to use when this feature is disabled.
        /// </summary>
        public static readonly ExtendedNotes None = new ExtendedNotes(new byte[0], (a4tuning, note) => 0);
    }
}