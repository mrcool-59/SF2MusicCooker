using System;
using System.Collections.Generic;

namespace SF2MusicCooker
{
    public static class FeatureInterpreter
    {
        /// <summary>
        /// Converts Furnace description of a FM instrument into what is expected by the SF2 sound driver (and ultimately what the Genesis hardware is capable of delivering).
        /// </summary>
        public static byte[] TranslateFurnaceToCubeFMInstrument(byte[] data)
        {
            if (data == null || data.Length != 37)
                throw new FormatException("Data payload must contain 37 bytes");

            // We MUST have exactly 4 operators
            if ((data[0] & 0b00001111) != 4)
                throw new NotSupportedException("FM instruments must have 4 operators, please adjust your instruments accordingly");

            // We don't know how to deal with disabled operators (bits 4~7)
            if ((data[0] & 0b11110000) != 0b11110000)
                Console.WriteLine("WARNING: FM translator code currently doesn't support disabled operators, please adjust your instruments accordingly");
            
            // TODO: maybe implement a hackish way to turn OFF certain operators if we receive such FM instruments from input .fur file
            // Possibly by screwing around with TL

            // ALG: bits 4~6, FB: bits 0~2 (UNUSED: bits 3, 7)
            int algo = (data[1] >> 4) & 7;
            int feedback = data[1] & 7;

            // Do not print warnings for Furnace format stuff we clearly can't support on the YM2616
            /*
            // FMS2: bits 5~7, AMS: bits 3~4, FMS: bits 0~2,
            if (data[2] != 0)
                Console.WriteLine("WARNING: FM translator code doesn't support FMS2, AMS, FMS");

            // AM2: bits 6~7, AM4: bit 5, LLPatch: bits 0~4
            if (data[3] != 0)
                Console.WriteLine("WARNING: FM translator code doesn't support AM2, AM4, LLPatch");

            // Block: bits 0~3 (UNUSED: bits 4~7)
            // NOTE: This appeared in version >= 224. If we read an older file, we've simply appended a zero here ;)
            if (data[4] != 0x00)
                Console.WriteLine("WARNING: FM translator code doesn't support Block");
            */

            // ------------------------------------------------------------------------------------------

            byte[] cubeFmInstrument = new byte[FMInstruments.Definition.LENGTH];

            const bool ssgEg = true;
            const int OP_BASE = 5;

            int[] outputOperatorsByAlgo =
            {
                0b1000,
                0b1000,
                0b1000,
                0b1000,
                0b1100,
                0b1110,
                0b1110,
                0b1111
            };

            for (int op = 0; op < 4; op++)
            {
                // ------------------------------------------------------------------------
                // IMPORTANT READ before proceeding: https://plutiedev.com/ym2612-registers
                // ------------------------------------------------------------------------

                // My understanding is the YM2612 implements a subset of the FM synthesis described in the Furnace FM instrument documentation
                // So we basically ignore stuff the YM2612 doesn't care about but that is present anyway in Furnace

                // Grab KSR (bit 7), DT (bits 4~6), MULT (bits 0~3)
                cubeFmInstrument[0 * 4 + op] = (byte)(data[OP_BASE + op * 8 + 0] & 0b01111111); // Mask out KSR part

                // Grab SUS (bit 7), TL (bits 0~6)
                cubeFmInstrument[1 * 4 + op] = (byte)(data[OP_BASE + op * 8 + 1] & 0b01111111); // Mask out SUS part

                // Grab RS (bits 6~7), VIB (bit 5), AR (bits 0~4)
                cubeFmInstrument[2 * 4 + op] = (byte)(data[OP_BASE + op * 8 + 2] & 0b11011111); // Mask out VIB part
                
                // Grab AM (bit 7), KSL (bits 5~6), DR (bits 0~4)
                cubeFmInstrument[3 * 4 + op] = (byte)(data[OP_BASE + op * 8 + 3] & 0b10011111); // Mask out KSL part

                // Grab EGT (bit 7), KVS (bits 5~6), D2R-SR (bits 0~4)
                cubeFmInstrument[4 * 4 + op] = (byte)(data[OP_BASE + op * 8 + 4] & 0b00011111); // Mask out EGT, KVS parts

                // Grab SL (bits 4~7), RR (bits 0~3)
                cubeFmInstrument[5 * 4 + op] = data[OP_BASE + op * 8 + 5]; // Perfect fit

                // Grab DVB (bits 4~7), SSG (bits 0~3)
                if (ssgEg) cubeFmInstrument[6 * 4 + op] = (byte)(data[OP_BASE + op * 8 + 6] & 0b00001111); // Mask out DVB part

                // Ignore DAM (bits 5~7), DT2 (bits 3~4), WS (bits 0~2)
                _ = data[OP_BASE + op * 8 + 7]; // Discard

                // Set TL to 0x7F for slot operators since their level will depend on the note being played (FIXME: it doesn't seem to affect end result however)
                bool isOutput = (outputOperatorsByAlgo[algo] & (1 << op)) != 0;
                if (isOutput) cubeFmInstrument[1 * 4 + op] = 0x7F;
            }

            // Write ALGO bits 0~2, FB bits 3~5
            cubeFmInstrument[28] = (byte)((algo & 0x07) | ((feedback & 0x07) << 3));

            // Man I sure hope it's gonna be lit after having to write all that shit.
            // Thanks SF2DISASM Wiz for lighting up the path ahead, and plutiedev.com for clarifying what's available in YM2612 registers!
            return cubeFmInstrument;
        }

        /// <summary>
        /// Parse a "SM" Furnace feature (sample instrument).
        /// </summary>
        public static SampleMap ParseFurnaceSampleInstrument(byte[] data)
        {
            if (data == null || (data.Length != 4 && data.Length != 484))
                throw new FormatException("Data payload must contain 4 or 484 bytes");

            Dictionary<int, SampleMap.Entry> note2sample = null;
            const int SAMPLE_MAP_ENTRIES = 120;

            short initialSample = BitConverter.ToInt16(data, 0);
            byte flags = data[2];
            // byte waveformLength = data[3]; // Ignored
            bool useSampleMap = (flags & 1) != 0;

            if (useSampleMap)
            {
                if (data.Length != 484)
                    throw new FormatException("Data payload must contain 484 bytes because sample map is expected");

                note2sample = new Dictionary<int, SampleMap.Entry>(SAMPLE_MAP_ENTRIES);

                for (int i = 0; i < SAMPLE_MAP_ENTRIES; i++)
                {
                    short note = BitConverter.ToInt16(data, 4 + i * 4);
                    short sample = BitConverter.ToInt16(data, 4 + i * 4 + 2);

                    note2sample[i + NoteBible.BASE_VALUE] = new SampleMap.Entry(sample, note + NoteBible.BASE_VALUE);
                }
            }

            return new SampleMap(initialSample, note2sample);
        }
    }
}
