using SF2MusicCooker.Furnace;
using System;
using System.Collections.Generic;

namespace SF2MusicCooker
{
    public sealed class InstrumentMap
    {
        private readonly Dictionary<byte, byte> instrument2fm;
        private readonly Dictionary<ushort, byte> instrument_note2sample;

        internal static ushort GetInstrumentAndNoteKey(byte instrument, byte note)
        {
            return (ushort)((instrument << 8) | note);
        }

        /// <summary>
        /// Return true if the provided instrument is a FM instrument and return its index.
        /// </summary>
        public bool FM(byte instrument, out byte fmInstrument)
        {
            return instrument2fm.TryGetValue(instrument, out fmInstrument);
        }

        /// <summary>
        /// Return true if the provided instrument is a sample instrument and return its index.
        /// </summary>
        public bool Sample(byte instrument, byte note, out byte sample)
        {
            ushort key = GetInstrumentAndNoteKey(instrument, note);
            return instrument_note2sample.TryGetValue(key, out sample);
        }

        /// <summary>
        /// Return true if the provided instrument is a PSG instrument.
        /// </summary>
        public bool PSG(byte instrument)
        {
            return false; // TODO
        }

        /// <summary>
        /// Verify that instruments in the Furnace file are used properly (i.e: in supported channels). Return the DAC byte to write for music headers.
        /// </summary>
        public void Check(FurnaceFile file, bool requiresDAC, out byte dac)
        {
            bool channel6_dac = requiresDAC || !file.HasPlayNoteCommand(5); // Also DAC mode if channel 6 is empty

            // TODO: complete this when samples are implemented

            foreach (byte fmInstrument in instrument2fm.Keys)
            {
                int[] channels = file.GetInstrumentUsage(fmInstrument);

                if (Array.Exists(channels, channel => channel >= 6))
                    throw new NotSupportedException("FM instrument " + Tools.Hex1(fmInstrument) + " can only be used in YM channels (1 to 6)");

                if (channel6_dac && Array.IndexOf(channels, 5) >= 0)
                    throw new NotSupportedException("FM instrument " + Tools.Hex1(fmInstrument) + " cannot be used in channel 6 because this channel is in DAC mode");
            }

            // TODO: complete this when PSG is implemented

            dac = (byte)(channel6_dac ? 0 : 1);
        }

        public InstrumentMap(FMInstruments instruments, PCMInstruments samples, Instrument[] furnaceInstruments, Instrument[] usedFurnaceInstruments)
        {
            HashSet<Instrument> usedSet = new HashSet<Instrument>(usedFurnaceInstruments);

            instrument2fm = instruments.Map(furnaceInstruments, usedSet);
            instrument_note2sample = samples.Map(furnaceInstruments, usedSet);

            // TODO: PSG
        }

        private InstrumentMap()
        {
            instrument2fm = new Dictionary<byte, byte>();
            instrument_note2sample = new Dictionary<ushort, byte>();
        }

        /// <summary>
        /// Represents an empty instrument map (it can handle empty Furnace files).
        /// </summary>
        public static readonly InstrumentMap Empty = new InstrumentMap();
    }
}