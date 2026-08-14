using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SF2MusicCooker
{
    public sealed class FurnaceFile
    {
        // This class only needs to support reading and handle a subset of Furnace file format features (anything related to Genesis chips)
        // Shortcuts are allowed and even encouraged!

        public readonly struct Effect
        {
            public const byte GoTo = 0x0B;
            public const byte GoNext = 0x0D;
            public const byte End = 0xFF;
            public const byte Pan = 0x08;
            public const byte PanTrinary = 0x80;

            public readonly byte Type;
            public readonly byte Value;

            public Effect(byte type, byte value)
            {
                Type = type;
                Value = value;
            }

            public string ToStringHex()
            {
                return Tools.Hex(new byte[2] { Type, Value });
            }

            public override string ToString()
            {
                if (this == Absent)
                    return "ABSENT";
                else
                    return Tools.Hex1(Type) + " [" + Tools.Hex1(Value) + "]";
            }

            public override int GetHashCode()
            {
                return Type * 433 + Value;
            }

            public override bool Equals(object obj)
            {
                return obj is Effect effect && effect == this;
            }

            public static bool operator ==(Effect a, Effect b)
            {
                return a.Type == b.Type && a.Value == b.Value;
            }

            public static bool operator !=(Effect a, Effect b)
            {
                return a.Type != b.Type || a.Value != b.Value;
            }

            /// <summary>
            /// Represents an absent effect.
            /// </summary>
            public static readonly Effect Absent = new Effect(0xEF, 0xFF);
        }

        public sealed class PatternCell
        {
            public const byte NoteCMinus5 = 0;
            public const byte NoteB9 = 179;
            public const byte NoteOff = 180;
            public const byte NoteRelease = 181;
            // public const byte MacroRelease = 182;
            public const byte NoteAbsent = 255;
            public const byte InstrumentAbsent = 255;
            public const byte VolumeAbsent = 255;

            public readonly byte Note;
            public readonly byte Instrument;
            public readonly byte Volume;
            public readonly Effect[] Effects;

            /// <summary>
            /// True if the cell contains nothing.
            /// </summary>
            public bool IsEmpty { get { return Note == NoteAbsent && Instrument == InstrumentAbsent && Volume == VolumeAbsent && Effects.Length == 0; } }

            /// <summary>
            /// True if the cell contains a new note to play.
            /// </summary>
            public bool HasNewNote { get { return Note != NoteAbsent && Note != NoteRelease && Note != NoteOff; } }

            public PatternCell()
            {
                Note = NoteAbsent;
                Instrument = InstrumentAbsent;
                Volume = VolumeAbsent;
                Effects = Array.Empty<Effect>();
            }

            public PatternCell(byte note, byte instrument, byte volume, Effect[] effects)
            {
                Note = note;
                Instrument = instrument;
                Volume = volume;
                Effects = effects ?? Array.Empty<Effect>();
            }

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(GetNoteString(Note));
                sb.Append(Instrument == InstrumentAbsent ? ".." : Tools.Hex1(Instrument));
                sb.Append(Volume == VolumeAbsent ? ".." : Tools.Hex1(Volume));
                foreach (Effect effect in Effects) sb.Append(effect.ToStringHex());
                return sb.ToString();
            }

            /// <summary>
            /// Get a user-friendly representation of a note command.
            /// </summary>
            public static string GetNoteString(byte note)
            {
                if (note == NoteAbsent)
                    return "...";
                else if (note == NoteOff)
                    return "OFF";
                else if (note == NoteRelease)
                    return "===";
                else
                    return NoteBible.GetByValue(note).Name;
            }

            /// <summary>
            /// Get the first effect of the provided type in the cell and return true on success.
            /// Otherwise return false and write the "Absent" effect.
            /// </summary>
            public bool TryGetEffect(byte type, out Effect effect)
            {
                foreach (Effect e in Effects)
                {
                    if (e.Type == type)
                    {
                        effect = e;
                        return true;
                    }
                }
                effect = Effect.Absent;
                return false;
            }

            /// <summary>
            /// Represents the empty cell.
            /// </summary>
            public static readonly PatternCell Empty = new PatternCell();
        }

        public sealed class Pattern
        {
            private readonly PatternCell[] cells;

            public int Rows { get { return cells.Length; } }

            public Pattern(int rows)
            {
                cells = new PatternCell[rows];
            }

            /// <summary>
            /// Get a cell from the pattern. This method will always return a not-null PatternCell.
            /// </summary>
            public PatternCell Get(int row)
            {
                return cells[row] ?? PatternCell.Empty;
            }

            /// <summary>
            /// Set a cell from the pattern. Passing null will clear the cell.
            /// </summary>
            public void Set(int row, PatternCell cell)
            {
                if (cell != null && cell.IsEmpty) cell = null;
                cells[row] = cell;
            }
        }

        public sealed class Instrument
        {
            public const short PSG = 0x00;
            public const short FM = 0x01;
            public const short DAC = 0x04;

            /// <summary>
            /// The instrument type (PSG, FM, DAC).
            /// </summary>
            public readonly short Type;

            /// <summary>
            /// The instrument user modifiable name.
            /// </summary>
            public readonly string Name;

            /// <summary>
            /// The data payload describing this instrument (see Furnace documentation for details).
            /// </summary>
            public readonly byte[] Data;

            public Instrument(short type, string name, byte[] data)
            {
                Type = type;
                Name = name;
                Data = data;
            }
        }

        public sealed class Sample
        {
            public readonly string Name;
            public readonly int Length;
            public readonly int Rate; // C-4
            public readonly byte Depth;
            public readonly byte LoopDirection;
            public readonly int LoopStart;
            public readonly int LoopEnd;
            public readonly byte[] Data;

            public Sample(string name, int length, int rate, byte depth, byte loopDirection, int loopStart, int loopEnd, byte[] data)
            {
                Name = name;
                Length = length;
                Rate = rate;
                Depth = depth;
                LoopDirection = loopDirection;
                LoopStart = loopStart;
                LoopEnd = loopEnd;
                Data = data;
            }

            public void Verify()
            {
                if (LoopStart != -1 || LoopEnd != -1)
                    throw new NotSupportedException("Sorry, samples may not loop (" + Name + ")");

                if (Depth != 0x08)
                    throw new NotSupportedException("8-bit PCM is the expected format for samples");

                if (Length != Data.Length)
                    throw new FormatException("Data length is not consistent with declared length");
            }
        }

        /// <summary>
        /// The format version of this Furnace file. We might not be able to process properly files generated by a version that is too old.
        /// </summary>
        public readonly int FormatVersion;

        /// <summary>
        /// The [channel, order] matrix that return keys.
        /// </summary>
        public readonly int[,] KeyByChannelAndOrder;

        /// <summary>
        /// The lookup that maps keys to patterns.
        /// </summary>
        public readonly Dictionary<int, Pattern> PatternByKey;

        /// <summary>
        /// The defined instruments.
        /// </summary>
        public readonly Instrument[] Instruments;

        /// <summary>
        /// The defined samples.
        /// </summary>
        public readonly Sample[] Samples;

        /// <summary>
        /// The rate at which the chip timer should tick (in hz). Values around 40 to 80 hz should be considered typical.
        /// </summary>
        public readonly float PlaybackRate;

        /// <summary>
        /// Number of channels.
        /// </summary>
        public int Channels { get { return KeyByChannelAndOrder.GetLength(0); } }

        /// <summary>
        /// Number of orders.
        /// </summary>
        public int Orders { get { return KeyByChannelAndOrder.GetLength(1); } }

        /// <summary>
        /// Number of rows per pattern.
        /// </summary>
        public int Rows { get { return PatternByKey[KeyByChannelAndOrder[0, 0]].Rows; } }

        /// <summary>
        /// Verify if the specified channel has at least a volume command.
        /// </summary>
        public bool HasVolumeCommand(int channel)
        {
            foreach (Pattern pattern in GetAllPatternsForChannel(channel))
            {
                for (int i = 0; i < pattern.Rows; i++)
                {
                    PatternCell cell = pattern.Get(i);
                    if (cell.Volume != PatternCell.VolumeAbsent) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Get all patterns for the specified channel. A pattern may appear multiple times if it is reused.
        /// </summary>
        public Pattern[] GetAllPatternsForChannel(int channel)
        {
            Pattern[] results = new Pattern[Orders];
            for (int order = 0; order < results.Length; order++)
            {
                int key = KeyByChannelAndOrder[channel, order];
                results[order] = PatternByKey[key];
            }
            return results;
        }

        /// <summary>
        /// Get all effect types present in the song. This can be used to warn user that some effects are not supported.
        /// </summary>
        public HashSet<byte> GetAllEffectTypes()
        {
            HashSet<byte> set = new HashSet<byte>();
            foreach (Pattern pattern in PatternByKey.Values)
            {
                for (int i = 0; i < pattern.Rows; i++)
                {
                    PatternCell cell = pattern.Get(i);
                    foreach (Effect effect in cell.Effects) set.Add(effect.Type);
                }
            }
            return set;
        }

        /// <summary>
        /// Remove unsupported notes from all patterns and return the number of cells changed.
        /// </summary>
        public int RemoveUnsupportedNotes()
        {
            int removed = 0;
            foreach (Pattern pattern in PatternByKey.Values)
            {
                for (int i = 0; i < pattern.Rows; i++)
                {
                    PatternCell cell = pattern.Get(i);
                    if (cell.HasNewNote)
                    {
                        byte note = NoteBible.Clamp(cell.Note);
                        if (note != cell.Note)
                        {
                            // If clamping has changed the value, it means it is an unsupported note
                            removed++;
                            pattern.Set(i, new PatternCell(PatternCell.NoteAbsent, cell.Instrument, cell.Volume, cell.Effects));
                        }
                    }
                }
            }
            return removed;
        }

        public FurnaceFile(int formatVersion, int[,] keyByChannelAndOrder, Dictionary<int, Pattern> patternByKey, Instrument[] instruments, Sample[] samples, float playbackRate)
        {
            FormatVersion = formatVersion;
            KeyByChannelAndOrder = keyByChannelAndOrder;
            PatternByKey = patternByKey;
            Instruments = instruments;
            Samples = samples;
            PlaybackRate = playbackRate;
        }

        /// <summary>
        /// Gives an empty file.
        /// </summary>
        public static readonly FurnaceFile Empty = new FurnaceFile(0, new int[10, 0], new Dictionary<int, Pattern>(), new Instrument[0], new Sample[0], 59);

        private static int ComputeKey(byte channel, short index)
        {
            if (index < 0 || index >= 256) throw new NotSupportedException("Channel " + channel + " | Bad pattern index: " + index + " (must be between 0-255)");

            return (channel << 16) | (byte)index;
        }

        private static void VersionGate(int version, int minimum)
        {
            if (version < minimum)
                throw new NotImplementedException("Sorry this Furnace file is an old file version (" + version + ") that this tool doesn't support (requires >= " + minimum + "), please edit this file in the newest version of Furnace and re-save it");
        }

        private static void ReadAndVerifyMagic(BinaryReader reader, string expected)
        {
            byte[] flag = reader.ReadBytes(expected.Length);
            string actual = Encoding.ASCII.GetString(flag);
            if (actual != expected) throw new FormatException("Bad '" + actual + "' magic bytes (expected: " + expected + ")");
        }

        private static string ReadSTR(BinaryReader reader)
        {
            List<byte> buffer = new List<byte>();
            while (true)
            {
                byte b = reader.ReadByte();
                if (b == 0) break;
                buffer.Add(b);
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        private static string[] ReadSTR(BinaryReader reader, int count)
        {
            string[] strs = new string[count];
            for (int i = 0; i < strs.Length; i++) strs[i] = ReadSTR(reader);
            return strs;
        }

        private static int[] ReadIntArray(BinaryReader reader, int count)
        {
            int[] ints = new int[count];
            for (int i = 0; i < ints.Length; i++) ints[i] = reader.ReadInt32();
            return ints;
        }

        /// <summary>
        /// Return true if the provided stream is definitely an uncompressed Furnace file. Otherwise return false.
        /// </summary>
        public static bool ProbeUncompressed(Stream possiblyCompressedStream)
        {
            byte[] buffer = new byte[4];
            if (possiblyCompressedStream.Read(buffer, 0, 4) == 4)
            {
                if (buffer[0] == 0x2D && buffer[1] == 0x46 && buffer[2] == 0x75 && buffer[3] == 0x72)
                    return true; // We have the beginning of the Furnace header magic word so it's definitely not compressed
            }
            possiblyCompressedStream.Seek(0, SeekOrigin.Begin);
            return false; // Assume it is compressed
        }

        /// <summary>
        /// Load a Furnace file from a stream that contains compressed data.
        /// </summary>
        public static FurnaceFile LoadCompressed(Stream compressedStream, string dumpPath = null)
        {
            using (MemoryStream uncompressedStream = new MemoryStream())
            {
                using (GZipStream decompressionStream = new GZipStream(compressedStream, CompressionMode.Decompress))
                {
                    decompressionStream.CopyTo(uncompressedStream);
                    uncompressedStream.Seek(0, SeekOrigin.Begin);

                    // For our file format analysis convenience under a hex editor
                    if (dumpPath != null) File.WriteAllBytes(dumpPath, uncompressedStream.ToArray());

                    return Load(uncompressedStream);
                }
            }
        }

        /// <summary>
        /// Load a Furnace file from a stream that contains uncompressed data.
        /// </summary>
        public static FurnaceFile Load(Stream uncompressedStream)
        {
            try
            {
                using (BinaryReader reader = new BinaryReader(uncompressedStream, Encoding.UTF8, true))
                {
                    if (!BitConverter.IsLittleEndian) throw new FormatException("Sorry, this tool doesn't support a Big Endian environment");

                    // --- Header ---

                    reader.BaseStream.Seek(0, SeekOrigin.Begin);

                    ReadAndVerifyMagic(reader, "-Furnace module-");
                    short version = reader.ReadInt16();
                    _ = reader.ReadInt16(); // reserved
                    int songInfoPtr = reader.ReadInt32();
                    _ = reader.ReadInt32(); // reserved
                    _ = reader.ReadInt32(); // reserved

                    VersionGate(version, 157);

                    // --- Song info ---

                    reader.BaseStream.Seek(songInfoPtr, SeekOrigin.Begin);

                    ReadAndVerifyMagic(reader, "INFO");
                    _ = reader.ReadInt32(); // size of this block (treated as reserved)
                    byte timeBase = reader.ReadByte();
                    byte speed1 = reader.ReadByte();
                    byte speed2 = reader.ReadByte();
                    byte initialArpeggioTime = reader.ReadByte();
                    float ticksPerSecond = reader.ReadSingle();
                    short patternLength = reader.ReadInt16();
                    short ordersLength = reader.ReadInt16();
                    byte highlightA = reader.ReadByte(); // UI visual only
                    byte highlightB = reader.ReadByte(); // UI visual only
                    short instrumentCount = reader.ReadInt16();
                    short wavetableCount = reader.ReadInt16();
                    short sampleCount = reader.ReadInt16();
                    int patternCount = reader.ReadInt32();

                    byte[] systems = reader.ReadBytes(32);
                    if (systems[0] != 0x83) throw new FormatException("First chip of song must be 'YM2612' chip");
                    if (systems[1] != 0x03) throw new FormatException("Second chip of song must be 'SN76489' chip");
                    if (systems[2] != 0x00) throw new FormatException("Song must contain exactly 2 chips");

                    // Sanity checks because we do have some constraints to output that to .asm files that ultimately needs to fit
                    if (instrumentCount > 64) throw new FormatException("Sorry, please limit your song to 64 instruments (sanity check)");
                    if (sampleCount > 64) throw new FormatException("Sorry, please limit your song to 64 samples (sanity check)");
                    if ((patternCount * patternLength) > 131072) throw new FormatException("Sorry, please limit your song to 131072 pattern rows (sanity check)");

                    // By virtue of the previous constraints
                    const int chipCount = 2;
                    const int channels = 10;

                    byte[] volumes = reader.ReadBytes(32);
                    byte[] panning = reader.ReadBytes(32);

                    int[] chipFlagsPtrs = ReadIntArray(reader, 32);
                    if (version < 119) chipFlagsPtrs = new int[0]; // should never be reached

                    string songName = ReadSTR(reader);
                    string songAuthor = ReadSTR(reader);

                    float a4tuning = reader.ReadSingle();
                    byte[] compatibilityFlags = reader.ReadBytes(20); // pretty much ignored, however this might contain some niche information to properly interpret some effects

                    int[] instrumentPtrs = ReadIntArray(reader, instrumentCount);
                    int[] wavetablePtrs = ReadIntArray(reader, wavetableCount);
                    int[] samplePtrs = ReadIntArray(reader, sampleCount);
                    int[] patternPtrs = ReadIntArray(reader, patternCount);

                    byte[] ordersByChannel = reader.ReadBytes(ordersLength * channels);
                    byte[] chipEffects = reader.ReadBytes(channels);
                    byte[] channelHide = reader.ReadBytes(channels);
                    byte[] channelCollapse = reader.ReadBytes(channels);
                    string[] channelNames = ReadSTR(reader, channels);
                    string[] channelShortNames = ReadSTR(reader, channels);
                    string songComment = ReadSTR(reader);
                    float masterVolume = reader.ReadSingle();
                    if (version < 59) masterVolume *= 2f; // should never be reached

                    if (version > 70)
                    {
                        byte[] ext1CompatibilityFlags = reader.ReadBytes(28);
                    }

                    short virtualTempoNumerator = reader.ReadInt16();
                    short virtualTempoDenominator = reader.ReadInt16();

                    if (version >= 95)
                    {
                        string firstSubsongName = ReadSTR(reader);
                        string firstSubsongComment = ReadSTR(reader);
                        byte subsongCount = reader.ReadByte();
                        _ = reader.ReadBytes(3); // reserved
                        int[] subsongPtrs = ReadIntArray(reader, subsongCount);

                        if (subsongCount != 0)
                            throw new NotSupportedException("This tool only supports Furnace files that contain exactly only 1 song, please split the subsongs into multiple files...");
                    }

                    if (version >= 103)
                    {
                        string systemName = ReadSTR(reader);
                        string albumCategoryGameName = ReadSTR(reader);
                        string songNameJapanese = ReadSTR(reader);
                        string songAuthorJapanese = ReadSTR(reader);
                        string systemNameJapanese = ReadSTR(reader);
                        string albumCategoryGameNameJapanese = ReadSTR(reader);
                    }

                    if (version >= 135)
                    {
                        for (int i = 0; i < chipCount; i++)
                        {
                            float chipVolume = reader.ReadSingle();
                            float chipPanning = reader.ReadSingle();
                            float chipFrontRearBalance = reader.ReadSingle();
                        }

                        int patchbayConnectionCount = reader.ReadInt32();
                        int[] patchbayData = ReadIntArray(reader, patchbayConnectionCount);
                        byte automaticPatchbay = reader.ReadByte();
                    }

                    if (version >= 138)
                    {
                        byte[] ext2CompatibilityFlags = reader.ReadBytes(8);
                    }

                    if (version >= 139)
                    {
                        byte speedPatternLength = reader.ReadByte();
                        byte[] speedPattern = reader.ReadBytes(16);

                        int grooveCount = reader.ReadByte();
                        byte[] grooveData = reader.ReadBytes((1 + 16) * grooveCount);
                    }

                    if (version >= 156)
                    {
                        int instrumentDirectoriesPtr = reader.ReadInt32();
                        int wavetableDirectoriesPtr = reader.ReadInt32();
                        int sampleDirectoriesPtr = reader.ReadInt32();
                    }

                    // --- Chip flags ---

                    for (int i = 0; i < chipFlagsPtrs.Length; i++)
                    {
                        if (chipFlagsPtrs[i] == 0) continue;

                        reader.BaseStream.Seek(chipFlagsPtrs[i], SeekOrigin.Begin);

                        ReadAndVerifyMagic(reader, "FLAG");
                        _ = reader.ReadInt32(); // size of this block (treated as reserved)
                        string pairs = ReadSTR(reader);
                    }

                    /// --- Patterns ---

                    Dictionary<int, Pattern> patternByKey = new Dictionary<int, Pattern>();

                    for (int i = 0; i < patternPtrs.Length; i++)
                    {
                        if (version < 157)
                        {
                            throw new NotImplementedException(); // Should never be reached
                        }
                        else
                        {
                            reader.BaseStream.Seek(patternPtrs[i], SeekOrigin.Begin);

                            ReadAndVerifyMagic(reader, "PATN");
                            _ = reader.ReadInt32(); // size of this block (treated as reserved)

                            byte subsong = reader.ReadByte();
                            byte channel = reader.ReadByte();
                            short index = reader.ReadInt16();
                            int key = ComputeKey(channel, index);
                            string name = ReadSTR(reader);

                            if (!patternByKey.TryGetValue(key, out Pattern pattern))
                            {
                                pattern = new Pattern(patternLength);
                                patternByKey.Add(key, pattern);
                            }

                            // IMPORTANT: Please refer to 'format.md' Furnace documentation for the explanation of the upcoming mess ;)

                            int currentRow = 0;

                            while (currentRow < patternLength)
                            {
                                byte first = reader.ReadByte();

                                if (first == 0xFF)
                                {
                                    currentRow = patternLength;
                                }
                                else if ((first & 0x80) != 0)
                                {
                                    int skip = (first & 0x7F) + 2;
                                    currentRow += skip;
                                }
                                else if (first == 0)
                                {
                                    int skip = 1;
                                    currentRow += skip;
                                }
                                else
                                {
                                    bool notePresent = (first & (1 << 0)) != 0;
                                    bool instrumentPresent = (first & (1 << 1)) != 0;
                                    bool volumePresent = (first & (1 << 2)) != 0;
                                    bool[] effectTypePresent = new bool[8];
                                    bool[] effectValuePresent = new bool[8];

                                    effectTypePresent[0] = (first & (1 << 3)) != 0;
                                    effectValuePresent[0] = (first & (1 << 4)) != 0;

                                    if ((first & (1 << 5)) != 0)
                                    {
                                        byte second = reader.ReadByte();

                                        effectTypePresent[0] = (second & (1 << 0)) != 0;
                                        effectValuePresent[0] = (second & (1 << 1)) != 0;
                                        effectTypePresent[1] = (second & (1 << 2)) != 0;
                                        effectValuePresent[1] = (second & (1 << 3)) != 0;
                                        effectTypePresent[2] = (second & (1 << 4)) != 0;
                                        effectValuePresent[2] = (second & (1 << 5)) != 0;
                                        effectTypePresent[3] = (second & (1 << 6)) != 0;
                                        effectValuePresent[3] = (second & (1 << 7)) != 0;

                                        if ((first & (1 << 6)) != 0)
                                        {
                                            byte third = reader.ReadByte();

                                            effectTypePresent[4] = (third & (1 << 0)) != 0;
                                            effectValuePresent[4] = (third & (1 << 1)) != 0;
                                            effectTypePresent[5] = (third & (1 << 2)) != 0;
                                            effectValuePresent[5] = (third & (1 << 3)) != 0;
                                            effectTypePresent[6] = (third & (1 << 4)) != 0;
                                            effectValuePresent[6] = (third & (1 << 5)) != 0;
                                            effectTypePresent[7] = (third & (1 << 6)) != 0;
                                            effectValuePresent[7] = (third & (1 << 7)) != 0;
                                        }
                                    }

                                    byte note = PatternCell.NoteAbsent;
                                    byte instrument = PatternCell.InstrumentAbsent;
                                    byte volume = PatternCell.VolumeAbsent;
                                    List<Effect> effects = null;

                                    if (notePresent) note = reader.ReadByte();
                                    if (instrumentPresent) instrument = reader.ReadByte();
                                    if (volumePresent) volume = reader.ReadByte();

                                    for (int e = 0; e < effectTypePresent.Length; e++)
                                    {
                                        if (effectTypePresent[e])
                                        {
                                            byte effectType = reader.ReadByte();
                                            byte effectValue = 0x00;

                                            if (effectValuePresent[e])
                                            {
                                                effectValue = reader.ReadByte();

                                                Effect effect = new Effect(effectType, effectValue);
                                                if (effect != Effect.Absent) // Ignore our special sentinel value
                                                {
                                                    if (effects == null) effects = new List<Effect>();
                                                    effects.Add(effect);
                                                }
                                            }
                                        }
                                    }

                                    pattern.Set(currentRow, new PatternCell(note, instrument, volume, effects?.ToArray()));
                                    currentRow++;
                                }
                            }

                            if (currentRow != patternLength)
                            {
                                throw new FormatException("Row cursor got out of bounds after reading pattern data");
                            }
                        }
                    }

                    // --- Instruments ---

                    Instrument[] instruments = new Instrument[instrumentPtrs.Length];

                    for (int i = 0; i < instrumentPtrs.Length; i++)
                    {
                        if (version < 127)
                        {
                            throw new NotImplementedException(); // Should never be reached
                        }
                        else
                        {
                            reader.BaseStream.Seek(instrumentPtrs[i], SeekOrigin.Begin);

                            ReadAndVerifyMagic(reader, "INS2");
                            _ = reader.ReadInt32(); // size of this block (treated as reserved)

                            short insVersion = reader.ReadInt16();
                            short type = reader.ReadInt16();
                            string name = "No name";
                            byte[] data = null;

                            if (type == Instrument.PSG)
                            {
                                // OK fine, but PSG instruments are hollow, they can only hold macros (which we don't support)
                            }
                            else if (type == Instrument.DAC)
                            {
                                // OK fine
                            }
                            else if (type == Instrument.FM)
                            {
                                // It appears this is the default data assumed by Furnace if you dont modify a newly created FM instrument (or if you revert to the default values)
                                // The issue is that a newly created FM instrument doesn't have a 'FM' feature recorded in the .fur file
                                data = new byte[37]
                                {
                                    // Flags
                                    244,

                                    // Base data
                                    4,
                                    0,
                                    0,
                                    0,

                                    // OP1 data
                                    85, 42, 31, 8, 64, 243, 0, 0,

                                    // OP2 data
                                    81, 48, 31, 4, 64, 177, 0, 0,
                                    
                                    // OP3 data
                                    1, 18, 31, 10, 64, 244, 0, 0,

                                    // OP4 data
                                    1, 2, 31, 9, 64, 249, 0, 0
                                };
                            }
                            else
                            {
                                throw new NotSupportedException("Unsupported instrument #" + i + " type: " + type + Environment.NewLine +
                                    "You may only use: FM instruments, DAC instruments (sample maps), PSG instruments (useless)" + Environment.NewLine +
                                    "Please note that macros are not supported in instruments (that's why PSG instruments have pretty much no use).");
                            }

                            while (true)
                            {
                                string featureCode = Encoding.UTF8.GetString(reader.ReadBytes(2));
                                short blockLength = reader.ReadInt16(); // size of this block

                                if (featureCode == "EN")
                                {
                                    break;
                                }
                                else if (featureCode == "MA")
                                {
                                    throw new NotSupportedException("Please do not use macros in your instruments");
                                }
                                else if (featureCode == "NA")
                                {
                                    name = ReadSTR(reader);
                                }
                                else if (featureCode == "FM" && type == Instrument.FM)
                                {
                                    if (insVersion < 224 && blockLength == 36)
                                    {
                                        byte[] oldVersionData = reader.ReadBytes(blockLength);
                                        byte[] newVersionData = new byte[37];

                                        Buffer.BlockCopy(oldVersionData, 0, newVersionData, 0, 4);
                                        newVersionData[4] = 0; // Introduce a zero here for forward compatibility
                                        Buffer.BlockCopy(oldVersionData, 0, newVersionData, 5, 32);

                                        data = newVersionData;
                                    }
                                    else if (blockLength != 37)
                                    {
                                        throw new FormatException("FM feature data should contain 37 bytes");
                                    }
                                    else
                                    {
                                        data = reader.ReadBytes(blockLength);
                                    }
                                }
                                else if (featureCode == "SM" && type == Instrument.DAC)
                                {
                                    data = reader.ReadBytes(blockLength);
                                    _ = FeatureInterpreter.ParseFurnaceSampleInstrument(data); // Verification
                                }
                                else
                                {
                                    reader.BaseStream.Seek(blockLength, SeekOrigin.Current);
                                    // Console.WriteLine("Skipped '{0}' instrument feature", featureCode);
                                }
                            }

                            instruments[i] = new Instrument(type, name, data);
                        }
                    }

                    /// --- Wavetables ---

                    // (ignored, not used by MD sound chips)

                    /// --- Samples ---

                    Sample[] samples = new Sample[samplePtrs.Length];

                    for (int i = 0; i < samplePtrs.Length; i++)
                    {
                        if (version < 102)
                        {
                            throw new NotImplementedException(); // Should never be reached
                        }
                        else
                        {
                            reader.BaseStream.Seek(samplePtrs[i], SeekOrigin.Begin);

                            ReadAndVerifyMagic(reader, "SMP2");
                            _ = reader.ReadInt32(); // size of this block (treated as reserved)
                            string sampleName = ReadSTR(reader);
                            int length = reader.ReadInt32();
                            int compatibilityRate = reader.ReadInt32();
                            int c4Rate = reader.ReadInt32();
                            byte depth = reader.ReadByte();
                            byte loopDirection = reader.ReadByte();
                            byte flags1 = reader.ReadByte();
                            byte flags2 = reader.ReadByte();
                            int loopStart = reader.ReadInt32();
                            int loopEnd = reader.ReadInt32();
                            int[] presence = ReadIntArray(reader, 4);
                            byte[] sampleData = reader.ReadBytes(length);

                            Sample sample = new Sample(sampleName, length, c4Rate, depth, loopDirection, loopStart, loopEnd, sampleData);
                            sample.Verify();

                            samples[i] = sample;
                        }
                    }

                    // --- Compute keys by (channel, order) lookup ---

                    int[,] keyByChannelAndOrder = new int[channels, ordersLength];
                    for (int c = 0; c < channels; c++)
                    {
                        for (int i = 0; i < ordersLength; i++)
                        {
                            byte order = ordersByChannel[c * ordersLength + i];
                            keyByChannelAndOrder[c, i] = ComputeKey((byte)c, order);
                        }
                    }

                    // --- Compute playback rate ---

                    float playbackRate = ticksPerSecond * virtualTempoNumerator / (virtualTempoDenominator * speed1);

                    return new FurnaceFile(version, keyByChannelAndOrder, patternByKey, instruments, samples, playbackRate);
                }
            }
            catch (Exception ex)
            {
                throw new FormatException("File doesn't seem to be a supported Furnace file", ex);
            }
        }
    }
}