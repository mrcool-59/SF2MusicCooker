using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SF2MusicCooker
{
    public sealed class Options
    {
        private static readonly Regex sampleRateCoeffLongRegex = new Regex("^--sampleratecoeff=([0-9.]+)$");
        private static readonly Regex sampleRateCoeffShortRegex = new Regex("^-src=([0-9.]+)$");

        public readonly int Mute;
        public readonly int Isolate;
        public readonly bool MuteSamples;
        public readonly bool PreserveRate;
        public readonly bool RemoveRelease;
        public readonly bool RemoveOff;
        public readonly bool NoOptimize;
        public readonly bool DumpNotes;
        public readonly bool DumpUncompressed;
        public readonly float SampleRateCoeff;

        /// <summary>
        /// Return true if 'channel' should be muted.
        /// </summary>
        public bool IsMuted(int channel)
        {
            if (channel < 0 || channel > 9) throw new ArgumentOutOfRangeException(nameof(channel));

            int mask = 1 << channel;
            return (Mute & mask) != 0 || (Isolate != 0 && (Isolate & mask) == 0);
        }

        /// <summary>
        /// Override options and return a new options object.
        /// </summary>
        public Options Override(Options other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            return new Options(other.Mute | Mute,
                               other.Isolate | Isolate,
                               other.MuteSamples || MuteSamples,
                               other.PreserveRate || PreserveRate,
                               other.RemoveRelease || RemoveRelease,
                               other.RemoveOff || RemoveOff,
                               other.NoOptimize || NoOptimize,
                               other.DumpNotes || DumpNotes,
                               other.DumpUncompressed || DumpUncompressed,
                               other.SampleRateCoeff != 1f ? other.SampleRateCoeff : SampleRateCoeff);
        }

        /// <summary>
        /// Read a text file that contains specific options to apply to particular musics/SFXs.
        /// </summary>
        public static Dictionary<int, Options> ReadOverrideOptions(string folder, string filename = "options.txt")
        {
            Dictionary<int, Options> map = new Dictionary<int, Options>();
            string path = Path.Combine(folder, filename);
            if (File.Exists(path))
            {
                foreach (var pair in Tools.ReadNumberStringMap(path))
                {
                    string[] args = pair.Value.Split(' ');
                    map.Add(pair.Key, new Options(args));
                }
            }
            return map;
        }

        /// <summary>
        /// Apply options overrides to generate the final options to use for a given music/SFX.
        /// </summary>
        public static Options GetFinalOptions(int number, Options options, Dictionary<int, Options> overrides)
        {
            if (overrides.TryGetValue(number, out Options @override)) options = options.Override(@override);
            return options;
        }

        public Options(string[] args)
        {
            bool Exists(string x) => Array.Exists(args, arg => arg.Equals(x, StringComparison.OrdinalIgnoreCase));
            for (int i = 1; i <= 10; i++) if (Exists("--mute" + i) || Exists("-m" + i)) Mute |= 1 << (i - 1);
            for (int i = 1; i <= 10; i++) if (Exists("--isolate" + i) || Exists("-i" + i)) Isolate |= 1 << (i - 1);
            MuteSamples = Exists("--mutesamples") || Exists("-ms");
            PreserveRate = Exists("--preserverate") || Exists("-pr");
            RemoveRelease = Exists("--removerelease") || Exists("-rr");
            RemoveOff = Exists("--removeoff") || Exists("-ro");
            NoOptimize = Exists("--nooptimize") || Exists("-no");
            DumpNotes = Exists("--dumpnotes") || Exists("-dn");
            DumpUncompressed = Exists("--dumpuncompressed") || Exists("-du");
            SampleRateCoeff = Tools.ParseFloatArg(args, sampleRateCoeffLongRegex, sampleRateCoeffShortRegex, 1f);
        }

        private Options(int mute, int isolate, bool muteSamples, bool preserveRate, bool removeRelease, bool removeOff, bool noOptimize, bool dumpNotes, bool dumpUncompressed, float sampleRateCoeff)
        {
            Mute = mute;
            Isolate = isolate;
            MuteSamples = muteSamples;
            PreserveRate = preserveRate;
            RemoveRelease = removeRelease;
            RemoveOff = removeOff;
            NoOptimize = noOptimize;
            DumpNotes = dumpNotes;
            DumpUncompressed = dumpUncompressed;
            SampleRateCoeff = sampleRateCoeff;
        }

        /// <summary>
        /// The default options.
        /// </summary>
        public static readonly Options Default = new Options(0, 0, false, false, false, false, false, false, false, 1f);
    }
}