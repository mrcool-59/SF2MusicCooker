using System;

namespace SF2MusicCooker.Furnace
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
            // Thanks Wiz for lighting up the path ahead, and plutiedev.com for clarifying what's available in YM2612 registers!
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

                note2sample = new SampleMap.Entry[NoteBible.LENGTH];

                for (int i = 0; i < note2sample.Length; i++)
                {
                    short note = BitConverter.ToInt16(data, 4 + i * 4);
                    short sample = BitConverter.ToInt16(data, 4 + i * 4 + 2);

                    note2sample[i] = new SampleMap.Entry(sample, note + NoteBible.BASE_VALUE);
                }
            }

            return new SampleMap(initialSample, note2sample);
        }

        public static string ParseFurnacePSGMacro(byte[] data)
        {
            /*
                # macro data (MA)

                notes:

                - the macro range varies depending on the instrument type.
                - "macro open" indicates whether the macro is collapsed or not in the instrument editor.
                - meaning of extended macros varies depending on instrument type.
                - meaning of panning macros varies depending on instrument type:
                  - for hard-panned chips (e.g. FM and Game Boy): left panning is 2-bit panning macro (left/right)
                  - otherwise both left and right panning macros are used

                ```
                size | description
                -----|------------------------------------
                  2  | length of macro header
                 ??? | data...
                ```

                each macro is represented like this:

                ```
                size | description
                -----|------------------------------------
                  1  | macro code
                     | - 0: vol
                     | - 1: arp
                     | - 2: duty
                     | - 3: wave
                     | - 4: pitch
                     | - 5: ex1
                     | - 6: ex2
                     | - 7: ex3
                     | - 8: alg
                     | - 9: fb
                     | - 10: fms
                     | - 11: ams
                     | - 12: panL
                     | - 13: panR
                     | - 14: phaseReset
                     | - 15: ex4
                     | - 16: ex5
                     | - 17: ex6
                     | - 18: ex7
                     | - 19: ex8
                     | - 20: ex9
                     | - 21: ex10
                     | - 255: stop reading and move on
                  1  | macro length
                  1  | macro loop
                  1  | macro release
                  1  | macro mode
                  1  | macro open/type/word size
                     | - bit 6-7: word size
                     |   - 0: 8-bit unsigned
                     |   - 1: 8-bit signed
                     |   - 2: 16-bit signed
                     |   - 3: 32-bit signed
                     | - bit 3: instant release (>=182)
                     | - bit 1-2: type
                     |   - 0: normal
                     |   - 1: ADSR
                     |   - 2: LFO
                     | - bit 0: open
                  1  | macro delay
                  1  | macro speed
                 ??? | macro data
                     | - length: macro length × word size
                ```

                ## interpreting macro mode values

                - sequence (normal): I think this is obvious...
                - ADSR:
                  - `val[0]`: bottom
                  - `val[1]`: top
                  - `val[2]`: attack
                  - `val[3]`: hold time
                  - `val[4]`: decay
                  - `val[5]`: sustain level
                  - `val[6]`: sustain hold time
                  - `val[7]`: decay 2
                  - `val[8]`: release
                - LFO:
                  - `val[11]`: speed
                  - `val[12]`: waveform
                    - 0: triangle
                    - 1: saw
                    - 2: pulse
                  - `val[13]`: phase
                  - `val[14]`: loop
                  - `val[15]`: global (not sure how will I implement this)
            */

            return "TODO"; // TODO
        }
    }
}
