using SF2MusicCooker.Furnace;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace SF2MusicCooker
{
    public static class AsmSheetWriter
    {
        const string padding = "\t\t";

        private static readonly byte[] supportedEffects = new byte[]
        {
            Effect.GoTo, Effect.GoNext, Effect.End, Effect.Pan, Effect.PanTrinary
        };

        /// <summary>
        /// Get the optimal "Timer B" value to play the song at the intended rate.
        /// </summary>
        public static byte GetOptimalTimerB(float targetPlayRate)
        {
            // Timer frequency (hz) = 1.0 / [ (256 - value) * 0.00030034 ]
            // Vanilla SF2 songs tend to have a value of 200 (C8) => that gives 59.46 hz

            // Yeah, I was too lazy to solve it with algebra xD
            float lowestError = float.PositiveInfinity;
            int bestValue = 0;

            for (int value = 0; value <= 255; value++)
            {
                float playRate = 1.0f / ((256 - value) * 0.00030034f);
                float error = Math.Abs(targetPlayRate - playRate);

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

                Console.WriteLine("! The song contains {0} unsupported effect{1}:", set.Count, set.Count > 1 ? "s": "");
                Console.WriteLine("    " + sb);
            }
        }

        /// <summary>
        /// Adjust the play rate of the Furnace file for optimal compatibility and return adjusted Furnace file.
        /// </summary>
        public static void AdjustPlayRate(ref FurnaceFile file, bool sfx, bool enabled)
        {
            int R(float x) => (int)Math.Round(x);
            float D(float a, float b) => Math.Abs(Math.Max(a, b) / Math.Min(a, b) % 1f);

            float originalPlayRate = file.PlayRate;
            int N = 1;

            if (enabled)
            {
                float min = sfx ? 57.999f : 12.999f;
                float max = sfx ? 62.001f : 960.001f; // For music, don't go all the way up to 3329 hz

                // Basically we try to find the N that put us above min, below max and that gives the lowest SFX speed alteration

                int[] candidateN = new int[49];
                for (int i = 0; i < candidateN.Length; i++) candidateN[i] = i + 1;

                N = Tools.SelectMin(candidateN, n =>
                {
                    if (n * originalPlayRate > max)
                        return 3000 + n;
                    else if (n * originalPlayRate < min)
                        return 2000 + n;
                    else
                        return (int)Math.Round(D(n * originalPlayRate, 60f) * 1000);
                });
            }

            file = file.Multiply(N);

            if (N == 1)
                Console.WriteLine("> Play rate: {0} hz", R(file.PlayRate));
            else
                Console.WriteLine("> Adjusted play rate for better compatibility: {0} hz -> {1} hz", R(originalPlayRate), R(file.PlayRate));

            if (sfx)
            {
                // Verify resulting rate is within acceptable range for SFXs

                if (file.PlayRate < 57.999f || file.PlayRate > 62.001f)
                    Console.WriteLine("! Play rate of SFX should be between 58~62 hz, it will play at a noticeably incorrect speed ingame");
            }
            else
            {
                // Verify resulting rate is within acceptable range for musics

                if (file.PlayRate < 12.999f)
                    Console.WriteLine("! Play rate of music is below minimum of 13 hz, it will play faster ingame");

                if (file.PlayRate > 3329.001f)
                    Console.WriteLine("! Play rate of music is above maximum of 3329 hz, it will play slower ingame");

                // Verify resulting rate will result in SFXs playing at the correct speed

                float a = file.PlayRate;
                float b = 60f;
                float r = D(a, b);

                if (r > 0.05f)
                    Console.WriteLine("! Play rate of music is {0} hz, SFXs will play at a noticeably incorrect speed ingame (r={1:0.000})", R(a), r);
            }
        }

        /// <summary>
        /// Given a Furnace file, guess the expected SFX type to use.
        /// </summary>
        public static SFXType GuessSFXType(FurnaceFile file)
        {
            if (file.Orders == 0 || file.HasPlayNoteCommand(3) || file.HasPlayNoteCommand(4) || file.HasPlayNoteCommand(5))
                return SFXType.Type2_YM_Ch4_Ch5_Ch6DAC;
            else
                return SFXType.Type1_PSG_Square3_Noise;
        }

        /// <summary>
        /// Outputs an ASM music sheet.
        /// </summary>
        public static string Write(FurnaceFile file, Options options, InstrumentMap map, PitchTable pitch, int number, int pairNumber = 0, string title = null)
        {
            StringBuilder sb = new StringBuilder(1024);
            Stopwatch sw = Stopwatch.StartNew();

            // Configuration for music
            ChannelCommands channels = new ChannelCommands(10, ChannelCommands.Mask_Music, false, Environment.NewLine + padding);

            // Generate the channels
            channels.Generate(file, options, map, pitch);

            // YM timer B (song tempo)
            byte timer = GetOptimalTimerB(file.PlayRate);

            // Write disclaimer
            sb.AppendFormat("; Generated by {0}", Tools.GetAssemblyNameAndVersion()); sb.AppendLine();
            sb.AppendFormat("; This output is from an automated tool, please avoid manual edits in it, instead it is better to improve/fix the tool!"); sb.AppendLine();
            sb.AppendLine();

            // Write header
            sb.AppendFormat("Music_{0}:", number);
            if (title != null) sb.AppendFormat("\t\t; {0}", title);
            sb.AppendLine();
            if (pairNumber > 0) { sb.AppendFormat("Music_{0}:\t\t; Special case, these two musics are paired", pairNumber); sb.AppendLine(); }
            sb.AppendFormat(padding + "db 0\t; Must be zero"); sb.AppendLine();
            sb.AppendFormat(padding + "db {0}\t; DAC mode (0=YES, 1=NO)", channels.DAC); sb.AppendLine();
            sb.AppendFormat(padding + "db 0\t; Reserved"); sb.AppendLine();
            sb.AppendFormat(padding + "db {0}\t; YM Timer B (music tempo)", Tools.Hex1ASM(timer)); sb.AppendLine();

            // Write channels
            channels.Write(sb, "Music_" + number, padding);

            // We have generated the full ASM to describe the music!
            string asm = sb.ToString();

            // Count number of bytes it will take in bank and show it on first line as a comment
            asm = "; MUSIC SIZE = " + AsmSheetToolkit.EstimateBytes(asm) + " (approximately)" + Environment.NewLine + asm;

            // Additional emptiness remark
            if (channels.Empty) asm = "; This is an empty music and it won't produce any sound output" + Environment.NewLine + Environment.NewLine + asm;

            // Print how much time it took
            if (!channels.Empty) Console.WriteLine("> Building ASM sheet took {0} ms", sw.ElapsedMilliseconds);

            // All done!
            return asm;
        }

        /// <summary>
        /// Outputs an empty ASM music sheet.
        /// </summary>
        public static string WriteEmpty(int number)
        {
            return Write(FurnaceFile.Empty, Options.Default, InstrumentMap.Empty, PitchTable.Empty, number);
        }

        /// <summary>
        /// Outputs an ASM SFX sheet.
        /// </summary>
        public static string WriteSFX(FurnaceFile file, Options options, InstrumentMap map, PitchTable pitch, string pointerName, SFXType type = SFXType.Automatic, string title = null)
        {
            StringBuilder sb = new StringBuilder(1024);
            Stopwatch sw = Stopwatch.StartNew();

            // Auto-detect SFX type
            if (type == SFXType.Automatic) type = GuessSFXType(file);

            // Configuration for SFX
            uint mask = type == SFXType.Type1_PSG_Square3_Noise ? ChannelCommands.Mask_SFX_1 : ChannelCommands.Mask_SFX_2;
            ChannelCommands channels = new ChannelCommands(10, mask, type == SFXType.Type2_YM_Ch4_Ch5_Ch6DAC, Environment.NewLine + padding);

            // Generate the channels
            channels.Generate(file, options, map, pitch);

            // Write header (disclaimer is skipped for SFX)
            sb.Append(pointerName);
            sb.Append(':');
            if (title != null) sb.AppendFormat("\t\t; {0}", title);
            sb.AppendLine();
            sb.AppendFormat(padding + "db {0}\t; SFX type", (byte)type); sb.AppendLine();

            // Write channels
            channels.Write(sb, pointerName, padding);

            // We have generated the full ASM to describe the SFX!
            string asm = sb.ToString();

            // Print how much time it took
            if (!channels.Empty) Console.WriteLine("> Building ASM sheet took {0} ms", sw.ElapsedMilliseconds);

            // All done!
            return asm;
        }

        /// <summary>
        /// Outputs an empty ASM SFX sheet.
        /// </summary>
        public static string WriteSFXEmpty(string pointerName)
        {
            return WriteSFX(FurnaceFile.Empty, Options.Default, InstrumentMap.Empty, PitchTable.Empty, pointerName);
        }
    }
}