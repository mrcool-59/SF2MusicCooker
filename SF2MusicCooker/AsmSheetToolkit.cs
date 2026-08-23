using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SF2MusicCooker
{
    public static class AsmSheetToolkit
    {
        private static readonly Regex music = new Regex("Music_([0-9]+)\\:");

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
        /// The size estimation should be very close to exact (if not just plain exact) but you should not 100% rely on it.
        /// </summary>
        public static int EstimateBytes(string asm)
        {
            using (StringReader reader = new StringReader(asm))
            {
                int bytes = 0;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string command = StripArguments(Tools.RemoveASMLabel(Tools.RemoveASMComment(line)));
                    if (map.TryGetValue(command, out int commandSize)) bytes += commandSize;
                }
                return bytes;
            }
        }

        /// <summary>
        /// Get the numbers of the musics defined by the sheet.
        /// </summary>
        public static int[] GetMusicNumbers(string asm)
        {
            return Tools.GetAllElements(asm, music, int.Parse);
        }

        /// <summary>
        /// Fill the set with the list of instruments used by the sheet.
        /// </summary>
        public static void FillInstruments(string asm, HashSet<int> set)
        {
            int[] instruments = Tools.GetAllNumericElements(asm, "inst");
            set.UnionWith(instruments);
        }

        /// <summary>
        /// Fill the set with the list of samples used by the sheet.
        /// </summary>
        public static void FillSamples(string asm, HashSet<int> set)
        {
            int[] samples = Tools.GetAllNumericElements(asm, "sample");
            int[] samplesL = Tools.GetAllNumericElements(asm, "sampleL");
            set.UnionWith(samples);
            set.UnionWith(samplesL);
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

        /// <summary>
        /// Get a section of a composite sheet, identified by a label, up until the next label or the end of the composite sheet.
        /// </summary>
        public static string SplitByLabel(string compositeAsm, Regex labelRegex, string label)
        {
            MatchCollection matches = labelRegex.Matches(compositeAsm);
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Groups[1].Value == label)
                {
                    int next = i + 1;
                    int start = matches[i].Index;
                    int end = next < matches.Count ? matches[next].Index : compositeAsm.Length;

                    return compositeAsm.Substring(start, end - start);
                }
            }
            throw new FormatException("Unable to find label '" + label + "' in the composite sheet");
        }

        /// <summary>
        /// Verify that all SFX channel pointers reference channel labels defined within the SFX bank and return the 'needed by' dependency map.
        /// This map should be checked whenever the user attempts to replace a vanilla SFX to make sure we end up in a valid state for assembly.
        /// </summary>
        public static Dictionary<int, HashSet<int>> VerifyAndGetDependencies(BankSFX[] banks)
        {
            Regex channelPointerNameRegex = new Regex("dw[ \t]+(Sfx_[0-9]+_Channel_[0-9]+)");
            Regex channelPointerNameLabelRegex = new Regex("(Sfx_[0-9]+_Channel_[0-9]+):");

            Dictionary<int, HashSet<int>> neededByMap = new Dictionary<int, HashSet<int>>();

            foreach (BankSFX bank in banks)
            {
                Dictionary<string, int> keys = new Dictionary<string, int>();

                SFX[] sfxs = bank.Custom.Concat(bank.Vanilla).ToArray();

                // Pass 1: collect keys provided by every SFX
                foreach (SFX sfx in sfxs)
                {
                    string[] provides = Tools.GetAllStringElements(sfx.Sheet, channelPointerNameLabelRegex);

                    foreach (string key in provides)
                    {
                        keys.Add(key, sfx.Number);
                    }
                }

                // Pass 2: verify we have a key for every key hole
                foreach (SFX sfx in sfxs)
                {
                    string[] requires = Tools.GetAllStringElements(sfx.Sheet, channelPointerNameRegex);

                    foreach (string key in requires)
                    {
                        if (!keys.TryGetValue(key, out int dependencyNumber))
                            throw new FormatException("SFX '" + sfx.Name + "' has a missing dependency, it needs this pointer label but it can't be found in the bank: " + key);

                        if (!neededByMap.TryGetValue(dependencyNumber, out HashSet<int> neededBy))
                        {
                            neededBy = new HashSet<int>();
                            neededByMap.Add(dependencyNumber, neededBy);
                        }

                        neededBy.Add(sfx.Number);
                    }
                }
            }

            return neededByMap;
        }
    }
}