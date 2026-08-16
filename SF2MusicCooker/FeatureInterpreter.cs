using System;

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

            // Helper to check if an operator is disabled
            bool OpDisabled(int op) => (data[0] & (1 << (4 + op))) == 0;

            // ALG: bits 4~6, FB: bits 0~2 (UNUSED: bits 3, 7)
            int algo = (data[1] >> 4) & 7;
            int feedback = data[1] & 7;

            // Do not print warnings for Furnace format stuff we clearly can't support on the YM2612
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

            // -------------------------------------------------------------------------------------------------------
            // WARNING: this part is tricky and requires utmost focus and knowledge otherwise everything could break!!
            // -------------------------------------------------------------------------------------------------------

            byte[] cubeFmInstrument = new byte[FMInstruments.Definition.LENGTH];

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
                byte instrumentTL = (byte)(data[OP_BASE + op * 8 + 1] & 0b01111111); // Mask out SUS part
                if (OpDisabled(op)) instrumentTL = 0x7F; // This will effectively disable the operator
                cubeFmInstrument[1 * 4 + op] = instrumentTL; // Perfect fit

                // Grab RS (bits 6~7), VIB (bit 5), AR (bits 0~4)
                cubeFmInstrument[2 * 4 + op] = (byte)(data[OP_BASE + op * 8 + 2] & 0b11011111); // Mask out VIB part
                
                // Grab AM (bit 7), KSL (bits 5~6), DR (bits 0~4)
                cubeFmInstrument[3 * 4 + op] = (byte)(data[OP_BASE + op * 8 + 3] & 0b10011111); // Mask out KSL part

                // Grab EGT (bit 7), KVS (bits 5~6), D2R-SR (bits 0~4)
                cubeFmInstrument[4 * 4 + op] = (byte)(data[OP_BASE + op * 8 + 4] & 0b00011111); // Mask out EGT, KVS parts

                // Grab SL (bits 4~7), RR (bits 0~3)
                cubeFmInstrument[5 * 4 + op] = data[OP_BASE + op * 8 + 5]; // Perfect fit

                // Grab DVB (bits 4~7), SSG (bits 0~3)
                cubeFmInstrument[6 * 4 + op] = (byte)(data[OP_BASE + op * 8 + 6] & 0b00001111); // Mask out DVB part

                // Ignore DAM (bits 5~7), DT2 (bits 3~4), WS (bits 0~2)
                _ = data[OP_BASE + op * 8 + 7]; // Discard

                // Output operators special case: TL is volume but flipped
                bool isOutput = (outputOperatorsByAlgo[algo] & (1 << op)) != 0;
                if (isOutput)
                {
                    byte volume = (byte)(0x7F - instrumentTL); // If we simply wrote 0x7F, we would lose the built-in instrument output volume level
                    cubeFmInstrument[1 * 4 + op] = volume; // Write the volume into TL (bits 0~6)
                }
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

            SampleMap.Entry[] note2sample = null;

            short initialSample = BitConverter.ToInt16(data, 0);
            byte flags = data[2];
            // byte waveformLength = data[3]; // Ignored
            bool useSampleMap = (flags & 1) != 0;

            if (useSampleMap)
            {
                if (data.Length != 484)
                    throw new FormatException("Data payload must contain 484 bytes because sample map is expected");

                note2sample = new SampleMap.Entry[SampleMap.MAP_LENGTH];

                for (int i = 0; i < note2sample.Length; i++)
                {
                    short note = BitConverter.ToInt16(data, 4 + i * 4);
                    short sample = BitConverter.ToInt16(data, 4 + i * 4 + 2);

                    note2sample[i] = new SampleMap.Entry(sample, note + NoteBible.BASE_VALUE);
                }
            }

            return new SampleMap(initialSample, note2sample);
        }
    }
}
