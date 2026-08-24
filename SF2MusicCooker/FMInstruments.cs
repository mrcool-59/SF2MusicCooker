using SF2MusicCooker.Furnace;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SF2MusicCooker
{
    public sealed class FMInstruments
    {
        public const int LENGTH = 4096;

        public const byte MAX_INSTRUMENTS = LENGTH / Definition.LENGTH;

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
            if (instrument < 0) throw new InvalidOperationException("Sorry, it appears all " + _used.Length + " instruments are used, there is no free instrument slot!");
            _used[instrument] = 1;
            return (byte)instrument;
        }

        /// <summary>
        /// Add Furnace instruments. Duplicate instruments are not added twice, they are reused instead. This method will throw if we don't have room for more instruments.
        /// </summary>
        public void AddMany(Instrument[] instruments, bool print)
        {
            for (int i = 0; i < instruments.Length; i++)
            {
                Instrument instrument = instruments[i];
                if (instrument.Type == Instrument.FM)
                {
                    Definition def = new Definition(FeatureInterpreter.TranslateFurnaceToCubeFMInstrument(instrument.Data));
                    byte index = FindSafe(def);

                    if (index == 0xFF)
                    {
                        index = Add(def);
                        if (print) Console.WriteLine("+ Added FM instrument '{0}' to instrument list! [{1}]", instrument.Name, index);
                    }
                    else
                    {
                        if (print) Console.WriteLine("! A duplicate of FM instrument '{0}' already exists in the instrument list! [{1}]", instrument.Name, index);
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
        /// Clear all instruments, except those in the set. Return the number of instruments removed.
        /// </summary>
        public int ClearExcept(HashSet<int> set)
        {
            int removed = 0;
            for (int i = 0; i < _used.Length; i++)
            {
                if (_used[i] == 1 && !set.Contains(i))
                {
                    Remove((byte)i);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>
        /// Generate a file-to-global FM instrument map for the given Furnace instrument array.
        /// </summary>
        public Dictionary<byte, byte> Map(Instrument[] instruments, HashSet<Instrument> usedSet = null)
        {
            Dictionary<byte, byte> map = new Dictionary<byte, byte>();
            for (int i = 0; i < instruments.Length; i++)
            {
                Instrument instrument = instruments[i];
                if (instrument.Type == Instrument.FM && (usedSet == null || usedSet.Contains(instrument)))
                {
                    Definition def = new Definition(FeatureInterpreter.TranslateFurnaceToCubeFMInstrument(instrument.Data));
                    map.Add((byte)i, Find(def));
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
            sb.AppendFormat("[{0}] ", _used.Count(u => u != 0));
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
        /// Load the contents of specified 'yminst.bin' file and update instruments data and used slots.
        /// </summary>
        public void Load(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            byte[] yminst = File.ReadAllBytes(path);
            Load(yminst);
        }

        /// <summary>
        /// Load the contents of specified 'yminst.bin' buffer and update instruments data and used slots.
        /// </summary>
        public void Load(byte[] yminst)
        {
            if (yminst == null || yminst.Length != LENGTH) throw new FormatException("Bad 'yminst' data: " + LENGTH + " bytes expected");

            Buffer.BlockCopy(yminst, 0, _buffer, 0, _buffer.Length);
            for (int i = 0; i < _used.Length; i++) _used[i] = (byte)(Definition.Read(_buffer, (byte)i).Equals(Definition.Null) ? 0 : 1);
        }

        public FMInstruments(int slots = MAX_INSTRUMENTS)
        {
            if (slots < 0)
                throw new ArgumentOutOfRangeException(nameof(slots), "cannot be negative");

            if (slots > MAX_INSTRUMENTS)
                throw new NotSupportedException(MAX_INSTRUMENTS + " instruments is the absolute maximum limit that can fit in " + LENGTH + " bytes");

            _buffer = new byte[LENGTH];
            _used = new byte[slots];

            Tools.Fill(_buffer, 0, _buffer.Length, 0xFF);
        }
    }
}
