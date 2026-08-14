using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SF2MusicCooker
{
    public static class AsmSheetEstimator
    {
        private static readonly Regex music = new Regex("Music_([0-9]+)\\:");
        private static readonly Regex inst = new Regex("inst[ \t]*([0-9]+)");
        private static readonly Regex word = new Regex("[a-zA-Z_]+");

        private static readonly Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // Core
            { "db", 1 },
            { "dw", 2 },
            { "dd", 4 },
            { "dq", 8 },

            // Macros
			{ "inst", 2 },
            { "vol", 2 },
            { "setRelease", 2 },
            { "sustain", 2 },
            { "setSlide", 2 },
            { "noSlide", 2 },
            { "vibrato", 2 },
            { "stereo", 2 },
            { "shifting", 2 },
            { "waitL", 2 },
            { "wait", 1 },
            { "noteL", 2 },
            { "note", 1 },
            { "sampleL", 2 },
            { "sample", 1 },
            { "psgNoteL", 2 },
            { "psgNote", 1 },
            { "psgInst", 2 },
            { "ymTimer", 2 },
            { "mainLoopStart", 2 },
            { "mainLoopEnd", 2 },
            { "repeatStart", 2 },
            { "repeatEnd", 2 },
            { "repeatSection1Start", 2 },
            { "repeatSection2Start", 2 },
            { "repeatSection3Start", 2 },
            { "countedLoopEnd", 2 },
            { "countedLoopStart", 2 },
            { "channel_end", 3 },
        };

        /// <summary>
        /// Get an estimation of the number of bytes a music .asm file will take when assembled into a music bank.
        /// The estimation should be "almost" exact but you should not 100% rely on it.
        /// </summary>
        public static int EstimateBytes(string sheet)
        {
            using (StringReader reader = new StringReader(sheet))
            {
                int bytes = 0;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    bytes += EstimateLine(line);
                }
                return bytes;
            }
        }

        private static int EstimateLine(string line)
        {
            int bytes = 0;
            line = Tools.RemoveASMComment(line);
            if (!string.IsNullOrWhiteSpace(line))
            {
                MatchCollection matches = word.Matches(line);
                foreach (Match match in matches)
                {
                    if (map.TryGetValue(match.Value, out int wordBytes))
                        bytes += wordBytes;
                }
            }
            return bytes;
        }

        /// <summary>
        /// Get the numbers of the musics defined by the sheet.
        /// </summary>
        public static int[] GetMusicNumbers(string sheet)
        {
            MatchCollection matches = music.Matches(sheet);
            int[] results = new int[matches.Count];
            for (int i = 0; i < results.Length; i++) results[i] = int.Parse(matches[i].Groups[1].Value);
            return results;
        }

        /// <summary>
        /// Fill the set with the list of instruments used by the sheet.
        /// </summary>
        public static void FillInstruments(string sheet, HashSet<int> set)
        {
            MatchCollection matches = inst.Matches(sheet);
            foreach (Match match in matches) set.Add(int.Parse(match.Groups[1].Value));
        }

        /// <summary>
        /// Get the number of bytes taken by a command. Return 0 if unknown command is passed.
        /// </summary>
        public static int GetCommandSize(string command)
        {
            _ = map.TryGetValue(command, out int size);
            return size;
        }

        /// <summary>
        /// Keep only the command part, given a string that looks like "noteL Cs3,45".
        /// </summary>
        public static string StripArguments(string commandAndArgument)
        {
            int start = 0;
            while (start < commandAndArgument.Length && char.IsWhiteSpace(commandAndArgument[start])) start++;

            int end = start;
            while (end < commandAndArgument.Length && !char.IsWhiteSpace(commandAndArgument[end])) end++;

            return commandAndArgument.Substring(start, end - start);
        }
    }
}