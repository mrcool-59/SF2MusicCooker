using System;
using System.Collections.Generic;
using System.IO;

namespace SF2MusicCooker
{
    public sealed class Options
    {
        public readonly bool MuteSamples;
        public readonly bool PreserveRate;
        public readonly bool NoOptimize;
        public readonly bool DumpNotes;
        public readonly bool DumpUncompressed;
        public readonly int IsolateChannel = -1;

        /// <summary>
        /// Override options and return a new options object.
        /// </summary>
        public Options Override(Options other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            return new Options(other.MuteSamples || MuteSamples,
                               other.PreserveRate || PreserveRate,
                               other.NoOptimize || NoOptimize,
                               other.DumpNotes || DumpNotes,
                               other.DumpUncompressed || DumpUncompressed,
                               other.IsolateChannel >= 0 ? other.IsolateChannel : IsolateChannel);
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
            MuteSamples = Exists("--mutesamples") || Exists("-ms");
            PreserveRate = Exists("--preserverate") || Exists("-pr");
            NoOptimize = Exists("--nooptimize") || Exists("-no");
            DumpNotes = Exists("--dumpnotes") || Exists("-dn");
            DumpUncompressed = Exists("--dumpuncompressed") || Exists("-du");
            for (int i = 1; i <= 10; i++) if (Exists("--channel" + i) || Exists("-c" + i)) IsolateChannel = i - 1;
        }

        private Options(bool muteSamples, bool preserveRate, bool noOptimize, bool dumpNotes, bool dumpUncompressed, int isolateChannel)
        {
            MuteSamples = muteSamples;
            PreserveRate = preserveRate;
            NoOptimize = noOptimize;
            DumpNotes = dumpNotes;
            DumpUncompressed = dumpUncompressed;
            IsolateChannel = isolateChannel;
        }

        /// <summary>
        /// The default options.
        /// </summary>
        public static readonly Options Default = new Options(false, false, false, false, false, -1);
    }
}