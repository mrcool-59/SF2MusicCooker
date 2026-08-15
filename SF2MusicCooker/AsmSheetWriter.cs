using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using static SF2MusicCooker.FurnaceFile;

namespace SF2MusicCooker
{
    public static class AsmSheetWriter
    {
        const string padding = "\t\t";

        private static readonly byte[] supportedEffects = new byte[]
        {
            Effect.GoTo, Effect.GoNext, Effect.End, Effect.Pan, Effect.PanTrinary
        };

        private static readonly LoopOptimizer optimizer = new LoopOptimizer(repeats => "countedLoopStart " + (repeats - 1), _ => "countedLoopEnd",
            7, 512, int.MaxValue, 32, Tools.RemoveASMComment, null, Weighter);

        private static int Weighter(string commandAndArguments)
        {
            string command = AsmSheetEstimator.StripArguments(commandAndArguments);
            int size = AsmSheetEstimator.GetCommandSize(command);
            // We definitely don't want unknown or main loop commands to be part of any loop whatsoever
            if (size == 0 || command == "mainLoopStart" || command == "mainLoopEnd") return short.MinValue;
            return size;
        }

        private static string HEX(byte[] bytes)
        {
            string h = Tools.Hex(bytes) + "h";
            if (!char.IsDigit(h[0])) h = "0" + h;
            return h;
        }

        private static string BYTE(byte value)
        {
            return value.ToString();
        }

        private static string BYTE_HEX(byte value)
        {
            return HEX(new byte[1] { value });
        }

        private static string NOTE(byte value)
        {
            // In "The First Battle" test music:
            // With +0 value offset: some instruments sound correct (noise) but others don't
            // With +12 value offset: most instruments sound correct but noise and some others breaks

            // TODO: This requires a more rigorous approach to get the correct notes all the time
            // Take a look at Furnace source code to figure how the frequency register is filled
            // Then select the closest available frequency in the Cube catalog
            // Also don't forget to do something like this:
            //      value = (byte)Math.Max(0, Math.Min(0x53, value)); // Cube sound engine has 0x54 notes defined [0..0x53]

            return NoteBible.GetByValue(value).Label; // Verify the note is valid/supported and return the proper ASM label
        }

        private static byte PAN_F2C(byte pan)
        {
            bool disableLeft = (pan & 0x0F) != 0;
            bool disableRight = (pan & 0xF0) != 0;
            if (disableLeft && disableRight) { disableLeft = disableRight = false; }
            return (byte)((disableLeft ? 0 : (1 << 7)) | (disableRight ? 0 : (1 << 6)));
        }

        private static byte PAN3_F2C(byte trinaryPan)
        {
            if (trinaryPan == 0x00) return 1 << 6; // LEFT
            else if (trinaryPan == 0xFF) return 1 << 7; // RIGHT
            else return (1 << 6) | (1 << 7); // CENTER (including invalid values)
        }

        /// <summary>
        /// Get the optimal "Timer B" value to play the song at the intended rate.
        /// </summary>
        public static byte GetOptimalTimerB(float targetPlaybackRate)
        {
            // Timer frequency (hz) = 1.0 / [ (256 - value) * 0.00030034 ]
            // Vanilla SF2 songs tend to have a value of 200 (C8) => that gives 59.46 hz

            // Yeah, I was too lazy to solve it with algebra xD
            float lowestError = float.PositiveInfinity;
            int bestValue = 0;

            for (int value = 0; value <= 255; value++)
            {
                float playbackRate = 1.0f / ((256 - value) * 0.00030034f);
                float error = Math.Abs(targetPlaybackRate - playbackRate);

                if (error < lowestError)
                {
                    bestValue = value;
                    lowestError = error;
                }
            }
            return (byte)bestValue;
        }

        /// <summary>
        /// Print the list of unsupported effects in the given Furnace file.
        /// </summary>
        public static void PrintUnsupportedEffects(FurnaceFile file)
        {
            HashSet<byte> set = file.GetAllEffectTypes();
            set.ExceptWith(supportedEffects);
            if (set.Count > 0)
            {
                StringBuilder sb = new StringBuilder(set.Count * 5);

                foreach (byte type in set)
                {
                    if (sb.Length > 0) sb.Append(" ");
                    sb.Append(Tools.Hex1(type));
                    sb.Append("..");
                }

                Console.WriteLine("! The song contains {0} currently unsupported effect{1}:", set.Count, set.Count > 1 ? "s": "");
                Console.WriteLine("    " + sb);
            }
        }

        /// <summary>
        /// Verify that all sample notes play their samples at C-4 otherwise print a warning.
        /// </summary>
        public static void PrintUnsupportedSampleMaps(FurnaceFile file)
        {
            int index = 0;
            foreach (Instrument instrument in file.Instruments)
            {
                if (instrument.Type == Instrument.DAC)
                {
                    SampleMap sampleMap = FeatureInterpreter.ParseFurnaceSampleInstrument(instrument.Data);
                    SampleMap.Entry[] pitchShiftedEntries = sampleMap.PitchShiftedEntries;

                    if (pitchShiftedEntries.Length > 0)
                    {
                        Console.WriteLine("! The song contains sample map instrument #{0} with pitch-shifted samples (i.e: not played at C-4 rate)", index);
                        Console.WriteLine("  Pitch-shifted samples are unsupported; end result will sound incorrect if you proceed without adjusting sample map");

                        // NOTE: we could silently fix this for the user by duplicating the sample with the pitch-shifted rate and changing the map to point to this new sample at C-4 rate
                        // But I think if you compose a Furnace track for the Sega Genesis you are probably not gonna pitch-shift your samples anyway
                    }
                }
                index++;
            }
        }

        /// <summary>
        /// Outputs an ASM music sheet compatible under SF2DISASM framework.
        /// The generated sheet will definitely be very imperfect when it comes to translating the special effects specified in the source Furnace file.
        /// </summary>
        public static string Write(FurnaceFile file, Options options, FMInstruments instruments, int number, int pairNumber = 0)
        {
            StringBuilder sb = new StringBuilder(1024);
            Stopwatch sw = Stopwatch.StartNew();

            // Enable DAC mode (0) if song contains samples, otherwise disable it (1)
            byte dac = (byte)(file.Samples.Length > 0 ? 0 : 1);

            // YM timer B (song tempo)
            byte timer = GetOptimalTimerB(file.PlaybackRate);

            // Build file-to-global instrument map
            Dictionary<byte, byte> instrumentMap = instruments.Map(file.Instruments);

            // Write disclaimer
            sb.AppendFormat("; Generated by {0}", Tools.GetAssemblyNameAndVersion()); sb.AppendLine();
            sb.AppendFormat("; This output is from an automated tool, please avoid manual edits in it, instead it is better to improve/fix the tool!"); sb.AppendLine();
            sb.AppendLine();

            // Write header
            sb.AppendFormat("Music_{0}:", number);
            if (options.Title != null) sb.AppendFormat("\t\t; {0}", options.Title);
            sb.AppendLine();
            if (pairNumber > 0) { sb.AppendFormat("Music_{0}:\t\t; Special case, these two musics are paired in SF2DISASM", pairNumber); sb.AppendLine(); }
            sb.AppendFormat(padding + "db 0\t; Must be zero"); sb.AppendLine();
            sb.AppendFormat(padding + "db {0}\t; DAC mode (0=YES, 1=NO)", BYTE(dac)); sb.AppendLine();
            sb.AppendFormat(padding + "db 0\t; Reserved"); sb.AppendLine();
            sb.AppendFormat(padding + "db {0}\t; YM Timer B (music tempo)", BYTE_HEX(timer)); sb.AppendLine();

            // Write pointers
            for (int c = 0; c < file.Channels; c++)
            {
                sb.AppendFormat(padding + "dw Music_{0}_Channel_{1}", number, c); sb.AppendLine();
            }
            sb.AppendLine();

            // Detect loop position by running the song a first time
            int loopOrder = -1;
            int beats = 0;
            foreach (FurnacePlayer.Tick tick in FurnacePlayer.Run(file, -1))
            {
                beats++;
                if (tick.BackwardGoToOrder >= 0)
                {
                    loopOrder = tick.BackwardGoToOrder;
                    break;
                }
            }

            if (file.Orders > 0)
            {
                Console.WriteLine("Executed {0} beats before ending playback.", beats);
                if (loopOrder >= 0)
                    Console.WriteLine("Music contains a loop at order #{0}.", loopOrder);
                else
                    Console.WriteLine("Music doesn't contain a loop.");
            }

            for (int channel = 0; channel < file.Channels; channel++)
            {
                // Write labels
                sb.AppendFormat("Music_{0}_Channel_{1}:", number, channel); sb.AppendLine();

                // Trivial file
                if (file.Orders == 0) continue;

                // When using isolate channel feature: if channel is not the isolated channel, neutralize it
                if (options.IsolateChannel >= 0 && channel != options.IsolateChannel) { sb.AppendLine(padding + "channel_end"); continue; }

                // Do not write channel 6 in DAC mode because it is not supported yet
                if (dac == 0 && channel == 5)
                {
                    sb.AppendLine(padding + "channel_end");
                    Console.WriteLine("! Channel 6 has been skipped because we don't support DAC yet"); // TODO
                    continue;
                }

                // Compute channel
                string channelAsm = WriteChannel(file, options, instrumentMap, channel, loopOrder);

                // Append to sheet
                sb.Append(padding);
                sb.AppendLine(channelAsm);
            }

            // Trivial file
            if (file.Orders == 0) sb.AppendLine(padding + "channel_end");

            // We have generated the full ASM to describe the music!
            string asm = sb.ToString();

            // Count number of bytes it will take in bank and show it on first line as a comment
            asm = "; MUSIC SIZE = " + AsmSheetEstimator.EstimateBytes(asm) + " (approximately)" + Environment.NewLine + asm;

            // Print how much time it took
            if (file.Orders > 0) Console.WriteLine("Building ASM sheet took {0} ms.", sw.ElapsedMilliseconds);

            // All done!
            return asm;
        }

        [Conditional("PRINT_LENGTH")]
        private static void PrintLength(List<string> commands, FurnacePlayer.Tick tick, bool silence)
        {
            if (silence)
            {
                commands.Add("; Silence length = " + tick.SilenceLength);
            }
            else
            {
                commands.Add("; Note release in = " + tick.NoteRelease);
                commands.Add("; Note length = " + tick.NoteLength);
            }
        }

        private static string WriteChannel(FurnaceFile file, Options options, Dictionary<byte, byte> instrumentMap, int channel, int loopOrder)
        {
            // Volume to cube helper
            Volume volume = Volume.FromStrategy(options.VolumeStrategy);
            byte VOL_F2C(byte ymVolume) => volume.Y2C(ymVolume);

            // If no volume instruction is present in the channel
            const int DEFAULT_VOL = 0x7F;

            // Prepare state
            List<string> commands = new List<string>(file.Orders * file.Rows);
            byte currentInstrument = PatternCell.InstrumentAbsent;
            byte currentVolume = VOL_F2C(DEFAULT_VOL);
            byte currentPan = PAN_F2C(0x00);
            byte currentRelease = 0xFF;
            byte currentLength = 0x00;
            int firstNoteTick = 0;

            // Is it FM or PSG?
            bool fm = channel < 6;

            // Helper: note
            void WriteNote(byte note, int length)
            {
                byte newLength = (byte)Math.Min(0xFF, length);
                if (currentLength != newLength)
                {
                    currentLength = newLength;
                    commands.Add("noteL " + NOTE(note) + "," + BYTE(currentLength));
                }
                else
                {
                    commands.Add("note  " + NOTE(note));
                }
            }

            // Helper: silence
            void WriteSilence(int length)
            {
                byte newLength = (byte)Math.Min(0xFF, length);
                if (currentLength != newLength)
                {
                    currentLength = newLength;
                    commands.Add("waitL " + BYTE(currentLength));
                }
                else
                {
                    commands.Add("wait");
                }
            }

            // Write initial pan for FM channels
            if (fm) commands.Add("stereo " + BYTE_HEX(currentPan));

            // Cancel vibrato immediately (looks like when the game boots a vibrato is set, at least in test mode)
            commands.Add("vibrato " + BYTE(0));

            // Play the song while observing this channel in particular
            foreach (FurnacePlayer.Tick tick in FurnacePlayer.Run(file, channel))
            {
                PatternCell cell = tick.ActiveChannelCell;

                // Mark beginning of loop
                if (tick.Order == loopOrder && tick.Row == 0)
                {
                    commands.Add("mainLoopStart");
                }

                // Is it FM or PSG channel?
                if (fm)
                {
                    // For troubleshooting
                    if (options.DumpNotes) commands.Add("; " + cell);

                    // Apply pan change (we support 2 possible ways to define pan change in Furnace)
                    if (cell.TryGetEffect(Effect.Pan, out Effect pan))
                    {
                        byte newPan = PAN_F2C(pan.Value);
                        if (currentPan != newPan)
                        {
                            currentPan = newPan;
                            commands.Add("stereo " + BYTE_HEX(currentPan));
                        }
                    }
                    else if (cell.TryGetEffect(Effect.PanTrinary, out pan))
                    {
                        byte newPan = PAN3_F2C(pan.Value);
                        if (currentPan != newPan)
                        {
                            currentPan = newPan;
                            commands.Add("stereo " + BYTE_HEX(currentPan));
                        }
                    }

                    // Apply instrument change
                    if (cell.Instrument != currentInstrument && cell.Instrument != PatternCell.InstrumentAbsent)
                    {
                        currentInstrument = cell.Instrument;
                        commands.Add("inst " + BYTE(instrumentMap[currentInstrument]));

                        // We output a volume change instruction after an instrument change, except if the cell also has a pending volume change instruction.
                        if (cell.Volume == PatternCell.VolumeAbsent || currentVolume == VOL_F2C(cell.Volume))
                        {
                            commands.Add("vol " + BYTE(currentVolume));
                        }
                    }

                    // Apply volume change
                    if (cell.Volume != PatternCell.VolumeAbsent && currentVolume != VOL_F2C(cell.Volume))
                    {
                        currentVolume = VOL_F2C(cell.Volume);
                        commands.Add("vol " + BYTE(currentVolume));
                    }

                    // Apply note change
                    if (cell.HasNewNote)
                    {
                        // Prove that we determine correctly the note release / length
                        PrintLength(commands, tick, false);

                        // If this is the first note, a silence might have occured beforehand
                        if (firstNoteTick >= 0)
                        {
                            // A silence was indeed there
                            if (firstNoteTick >= 1)
                            {
                                commands.Add("; First note delay"); // Put a remark this is the first note delay
                                WriteSilence(firstNoteTick);
                            }

                            // First note placed
                            firstNoteTick = -1;
                        }

                        byte newRelease = (byte)Math.Min(0x7F, tick.NoteLength - tick.NoteRelease);
                        if (currentRelease != newRelease)
                        {
                            currentRelease = newRelease;
                            commands.Add("setRelease " + BYTE(currentRelease));
                        }

                        WriteNote(cell.Note, tick.NoteLength);
                    }
                    else if (cell.Note == PatternCell.NoteOff && firstNoteTick < 0)
                    {
                        // Prove that we determine correctly the silence length
                        PrintLength(commands, tick, true);

                        // Please notice that note OFF commands are ignored if the first note hasn't been placed yet
                        WriteSilence(tick.SilenceLength);
                    }
                    else if (firstNoteTick >= 0)
                    {
                        // Count the ticks before the first note is placed!
                        firstNoteTick++;
                    }

                    // TODO: verify if DAC mode is enabled for the song that no FM instrument is used in 6th channel

                    // TODO: remaining FM features to implement/review:
                    // - Set Pitch Slides
                    // - Note/Frequency Shifting
                    // - Vibrato

                    // - Arpeggio (?)
                    // - Legato (?)
                    // - Portamento (?)
                    // - Tremolo (?)
                    // - Set tick rate

                    // https://tildearrow.org/furnace/doc/v0.6.7/3-pattern/effects.html
                }
                else
                {
                    // PSG channel grammar
                    // TODO
                }

                // Mark ending of loop and exit
                if (tick.BackwardGoToOrder >= 0)
                {
                    commands.Add("mainLoopEnd");
                    break;
                }
            }

            // Add channel_end if we don't have a loop
            if (loopOrder == -1) commands.Add("channel_end");

            // List to array
            string[] finalCommands = commands.ToArray();

            // Apply optimization (only if dumping is disabled)
            if (options.OptimizeNotes && !options.DumpNotes) finalCommands = optimizer.Optimize(finalCommands);

            // Outputs command list
            string asm = string.Join(Environment.NewLine + padding, finalCommands);

            // Small optimization for empty channels
            asm = Regex.Replace(asm, "mainLoopStart[\r\n\t ]+mainLoopEnd", "channel_end");

            // Channel done!
            return asm;
        }
    }
}

/* ----- AVAILABLE GRAMMAR -----

For all (timing/looping):
                sb.AppendLine("mainLoopStart");
                sb.AppendLine("mainLoopEnd");
                sb.AppendLine("wait");
                sb.AppendLine("waitL " + BYTE(192));

For FM/DAC channels:
                sb.AppendLine("stereo " + BYTE_HEX(0xC0));
                sb.AppendLine("vol " + BYTE_HEX(0x0C));
                sb.AppendLine("wait");
                sb.AppendLine("waitL " + BYTE(192));

For FM channels:
                sb.AppendLine("inst " + BYTE(0));
                sb.AppendLine("sustain");
                sb.AppendLine("setRelease " + BYTE_HEX(0x05));
                sb.AppendLine("vibrato " + BYTE_HEX(0x2C));
                sb.AppendLine("setSlide " + BYTE_HEX(0x20));
                sb.AppendLine("noSlide");
                sb.AppendLine("shifting " + BYTE_HEX(0x20));
                sb.AppendLine("noteL " + NOTE("As3") + "," + BYTE(24));
                sb.AppendLine("note " + NOTE("As3"));

For DAC channel:
                sb.AppendLine("sample " + BYTE(4));
                sb.AppendLine("sampleL " + BYTE(4) + "," + BYTE(3));

For PSG channels:
                sb.AppendLine("psgInst " + BYTE(4));
                sb.AppendLine("psgNoteL " + NOTE("As3") + "," + BYTE(4));
                sb.AppendLine("psgNote " + NOTE("As3"));
                sb.AppendLine("setRelease " + BYTE_HEX(0x05));
                sb.AppendLine("vibrato " + BYTE_HEX(0x4C));
                sb.AppendLine("shifting " + BYTE_HEX(0x10));
*/