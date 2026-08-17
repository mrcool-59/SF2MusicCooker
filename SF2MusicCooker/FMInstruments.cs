using System;
using System.Collections.Generic;
using System.Text;

namespace SF2MusicCooker
{
    public sealed class FMInstruments
    {
        public const byte MAX_INSTRUMENTS = 141; // (= 4096 / 29)

        public sealed class Definition : IEquatable<Definition>
        {
            public const int LENGTH = 29; // See documentation in 'yminst.txt'

            private readonly byte[] _buffer;

            public Definition(byte[] buffer)
            {
                if (buffer == null || buffer.Length != LENGTH)
                    throw new ArgumentException("must have length " + LENGTH, nameof(buffer));

                _buffer = buffer;
            }

            public static readonly Definition Null = new Definition(new byte[LENGTH] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });

            public bool Equals(Definition other)
            {
                return Convert.ToBase64String(_buffer) == Convert.ToBase64String(other._buffer);
            }

            public override int GetHashCode()
            {
                return Convert.ToBase64String(_buffer).GetHashCode();
            }

            public override string ToString()
            {
                return Convert.ToBase64String(_buffer);
            }

            public void Write(byte[] buffer, byte instrument)
            {
                Buffer.BlockCopy(_buffer, 0, buffer, instrument * LENGTH, LENGTH);
            }

            public static Definition Read(byte[] buffer, byte instrument)
            {
                byte[] chunk = new byte[LENGTH];
                Buffer.BlockCopy(buffer, instrument * LENGTH, chunk, 0, LENGTH);
                return new Definition(chunk);
            }
        }

        private readonly byte[] _buffer;
        private readonly byte[] _used;

        private byte FindSafe(Definition definition)
        {
            for (int i = 0; i < _used.Length; i++)
            {
                if (_used[i] == 0) continue;

                byte instrument = (byte)i;
                if (definition.Equals(Definition.Read(_buffer, instrument))) return instrument;
            }
            return 0xFF;
        }

        private byte Allocate()
        {
            int instrument = Array.FindIndex(_used, u => u == 0);
            if (instrument < 0) throw new NotSupportedException("Sorry, it appears all " + _used.Length + " instruments are used, there is no free instrument slot!");
            _used[instrument] = 1;
            return (byte)instrument;
        }

        /// <summary>
        /// Add all instruments from a FurnaceFile. Duplicate instruments are not added twice, they are reused instead. This method will throw if we don't have room for more instruments.
        /// </summary>
        public void AddMany(FurnaceFile file, bool print)
        {
            int index = 0;

            foreach (var i in file.Instruments)
            {
                if (i.Type == FurnaceFile.Instrument.FM)
                {
                    if (print) Console.WriteLine("> Analyzing FM instrument #{0} '{1}'...", index++, i.Name);

                    Definition def = new Definition(FeatureInterpreter.TranslateFurnaceToCubeFMInstrument(i.Data));
                    byte instrument = FindSafe(def);

                    if (instrument == 0xFF)
                    {
                        instrument = Add(def);
                        if (print) Console.WriteLine("+ Added FM instrument '{0}' to instrument list! [{1}]", i.Name, instrument);
                    }
                    else
                    {
                        if (print) Console.WriteLine("! A duplicate of FM instrument '{0}' already exists in the instrument list! [{1}]", i.Name, instrument);
                    }
                }
            }
        }
        
        /// <summary>
        /// Add an instrument and return its associated instrument number. This method will throw if we don't have room for more instruments.
        /// </summary>
        public byte Add(Definition definition)
        {
            byte instrument = Allocate();
            definition.Write(_buffer, instrument);
            return instrument;
        }

        /// <summary>
        /// Resolve the instrument number. Throws an exception if the instrument couldn't be found.
        /// </summary>
        public byte Find(Definition definition)
        {
            byte index = FindSafe(definition);
            if (index == 0xFF) throw new KeyNotFoundException("Instrument couldn't be found in the instrument list, maybe it wasn't added...");
            return index;
        }

        /// <summary>
        /// Clear an instrument.
        /// </summary>
        public void Remove(byte instrument)
        {
            if (instrument >= _used.Length) throw new ArgumentOutOfRangeException(nameof(instrument));

            _used[instrument] = 0;
            Definition.Null.Write(_buffer, instrument);
        }

        /// <summary>
        /// Clear all instruments.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _used.Length; i++) Remove((byte)i);
        }

        /// <summary>
        /// Clear all instruments, except those in the set.
        /// </summary>
        public void ClearExcept(HashSet<int> set)
        {
            for (int i = 0; i < _used.Length; i++)
            {
                if (_used[i] == 1 && !set.Contains(i))
                {
                    Remove((byte)i);
                }
            }
        }

        /// <summary>
        /// Generate a file-to-global instrument map for the given Furnace instrument array.
        /// </summary>
        public Dictionary<byte, byte> Map(FurnaceFile.Instrument[] instruments)
        {
            Dictionary<byte, byte> map = new Dictionary<byte, byte>();
            for (int instrument = 0; instrument < instruments.Length; instrument++)
            {
                FurnaceFile.Instrument i = instruments[instrument];
                if (i.Type == FurnaceFile.Instrument.FM)
                {
                    Definition def = new Definition(FeatureInterpreter.TranslateFurnaceToCubeFMInstrument(i.Data));
                    map.Add((byte)instrument, Find(def));
                }
            }
            return map;
        }

        /// <summary>
        /// Get the binary representation of the instruments list.
        /// </summary>
        public byte[] ToArray()
        {
            return (byte[])_buffer.Clone();
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Used: ");
            for (int i = 0; i < _used.Length; i++)
            {
                if (_used[i] == 1) { sb.Append(i); sb.Append(' '); }
            }
            sb.AppendLine();
            sb.Append("Free: ");
            for (int i = 0; i < _used.Length; i++)
            {
                if (_used[i] == 0) { sb.Append(i); sb.Append(' '); }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Load the contents of 'yminst.bin' and update instruments data and used slots.
        /// </summary>
        public void Load(byte[] yminst)
        {
            if (yminst == null || yminst.Length != 4096) throw new FormatException("Bad 'yminst' data: 4096 bytes expected");

            Buffer.BlockCopy(yminst, 0, _buffer, 0, _buffer.Length);
            for (int i = 0; i < _used.Length; i++) _used[i] = (byte)(Definition.Read(_buffer, (byte)i).Equals(Definition.Null) ? 0 : 1);
        }

        public FMInstruments(int slots = MAX_INSTRUMENTS)
        {
            if (slots < 0)
                throw new ArgumentOutOfRangeException(nameof(slots), "cannot be negative");

            if (slots > MAX_INSTRUMENTS)
                throw new NotSupportedException(MAX_INSTRUMENTS + " instruments is the absolute maximum limit that can fit in 4096 bytes");

            _buffer = new byte[4096];
            _used = new byte[slots];

            Tools.Fill(_buffer, 0, _buffer.Length, 0xFF);
        }
    }
}
