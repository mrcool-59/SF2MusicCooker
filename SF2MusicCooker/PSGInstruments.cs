using SF2MusicCooker.Furnace;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SF2MusicCooker
{
    public sealed class PSGInstruments
    {
        private readonly Envelope[] instruments;

        public PSGInstruments(string path)
        {
            Regex regex = new Regex("PSG_INSTRUMENT_[0-9A-F]+:");
            string compositeAsm = File.ReadAllText(path);
            string[] instrumentsAsm = AsmSheetToolkit.SplitByLabel(compositeAsm, regex);
            List<byte> attack = new List<byte>();
            List<byte> release = new List<byte>();

            instruments = new Envelope[instrumentsAsm.Length];

            for (int i = 0; i < instrumentsAsm.Length; i++)
            {
                attack.Clear();
                release.Clear();

                string asm = instrumentsAsm[i].Replace(",", "\ndb ");
                int[] values = Tools.GetAllNumericElements(asm, "db");

                List<byte> current = attack;

                foreach (int value in values)
                {
                    current.Add((byte)(value & 0xF));

                    if ((value & 0x80) != 0)
                    {
                        if (current == attack)
                            current = release;
                        else if (current == release)
                            break;
                    }
                }

                instruments[i] = new Envelope(attack.ToArray(), release.ToArray());
            }
        }

        private PSGInstruments()
        {
            instruments = new Envelope[1] { Envelope.Default };
        }

        /// <summary>
        /// Given the supplied furnace PSG instrument, find the proper PSG envelope to use.
        /// </summary>
        public bool FindEnvelope(FurnaceFile file, ushort furnaceInstrument, out byte index, out bool approximative)
        {
            index = (byte)(instruments.Length - 1); // Default value if PSG instrument doesn't have a volume envelope
            approximative = false;

            if (furnaceInstrument < file.Instruments.Length)
            {
                Instrument instrument = file.Instruments[furnaceInstrument];
                if (instrument.Type == Instrument.PSG && instrument.Data != null)
                {
                    Envelope envelope = FeatureInterpreter.ParseFurnacePSGMacroLevels(instrument.Data);
                    if (envelope != null)
                    {
                        int position = Array.IndexOf(instruments, envelope);
                        if (position >= 0)
                        {
                            index = (byte)position;
                            return true;
                        }
                        else
                        {
                            index = (byte)EnvelopeGuesser.Guess(envelope, instruments); // Find closest one instead
                            approximative = true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Given the supplied hints, figure out the PSG envelope to use.
        /// </summary>
        public byte GuessEnvelope(FurnaceFile file, Tick tick, int channel, byte totalLevel, out int noteRelease)
        {
            noteRelease = tick.NoteRelease;

            byte[] levels = ReadLevels(file, channel, tick.Position, tick.NoteLength, totalLevel);

            if (tick.NoteLength == noteRelease)
            {
                byte index = (byte)EnvelopeGuesser.Guess(levels, instruments, out noteRelease); // Note release unspecified: determine the best one
                noteRelease = Math.Min(noteRelease, tick.NoteLength); // Note release cannot be made higher than note length
                return index;
            }
            else
            {
                return (byte)EnvelopeGuesser.Guess(levels, instruments, noteRelease);
            }
        }

        /// <summary>
        /// Compute the PSG instrument value from envelope index and total level.
        /// </summary>
        public static byte ComputeInstrument(byte index, byte totalLevel)
        {
            return (byte)((index << 4) | totalLevel);
        }

        /// <summary>
        /// True if the provided PSG instrument will have a residual level at the end of its release envelope.
        /// </summary>
        public bool HasResidualLevel(ushort instrument)
        {
            byte index = (byte)(instrument >> 4);
            if (index < instruments.Length)
            {
                byte[] release = instruments[index].Release;
                byte totalLevel = (byte)(instrument & 0x0F);

                int finalLevel = totalLevel - 0xF + release[release.Length - 1];
                return finalLevel > 0;
            }
            return false;
        }

        /// <summary>
        /// Give a friendly description of the PSG instrument (envelope + level).
        /// </summary>
        public static string Dump(ushort instrument, bool enabled)
        {
            if (enabled)
                return string.Format(" ; envelope = {0}, level = {1}", Tools.Hex1((byte)(instrument >> 4)), Tools.Hex1((byte)(instrument & 0x0F)));
            else
                return string.Empty;
        }

        private static byte[] ReadLevels(FurnaceFile file, int channel, Position position, int length, byte initialLevel)
        {
            byte currentLevel = initialLevel;
            byte[] levels = new byte[length];
            int i = 0;
            foreach (Tick tick in Player.Run(file, channel, 0, position))
            {
                PatternCell cell = tick.ActiveChannelCell;
                if (cell.Volume != PatternCell.VolumeAbsent) currentLevel = cell.Volume;

                levels[i++] = currentLevel;
                if (i >= length) break;
            }
            Normalize(levels);
            return levels;
        }

        private static void Normalize(byte[] levels)
        {
            byte max = levels.Max();
            byte min = levels.Min();

            if (max != min)
            {
                for (int i = 0; i < levels.Length; i++)
                {
                    float normalized = (float)(levels[i] - min) / (max - min);
                    levels[i] = (byte)Math.Round(normalized * 0xF);
                }
            }
            else
            {
                const byte FULL = 0x0F;
                Tools.Fill(levels, FULL); // To happen, we would have to play a note at zero volume level
            }
        }

        public static readonly PSGInstruments Empty = new PSGInstruments();
    }
}
