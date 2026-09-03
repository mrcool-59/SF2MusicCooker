using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SF2MusicCooker
{
    public sealed class Options
    {
        private static readonly Regex muteInstrumentLongRegex = new Regex("^--muteinstrument=([a-fA-F0-9hH]+)$");
        private static readonly Regex muteInstrumentShortRegex = new Regex("^-mi=([a-fA-F0-9hH]+)$");

        private static readonly Regex sampleRateCoeffLongRegex = new Regex("^--sampleratecoeff=([0-9.]+)$");
        private static readonly Regex sampleRateCoeffShortRegex = new Regex("^-src=([0-9.]+)$");

        private static readonly Regex volumeCoeffLongRegex = new Regex("^--volume=([0-9.]+)$");
        private static readonly Regex volumeCoeffShortRegex = new Regex("^-v=([0-9.]+)$");

        public readonly int Mute;
        public readonly int Isolate;
        public readonly int[] MuteInstruments;
        public readonly bool MuteSamples;
        public readonly bool PreserveRate;
        public readonly bool RemoveRelease;
        public readonly bool RemoveOff;
        public readonly bool NoOptimize;
        public readonly bool DumpNotes;
        public readonly bool DumpUncompressed;
        public readonly float SampleRateCoeff;
        public readonly float VolumeCoeff;
        public readonly string VolumeMode;

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
        /// Return true if provided FM instrument is not blacklisted.
        /// </summary>
        public bool IsAllowed(int instrument)
        {
            return Array.IndexOf(MuteInstruments, instrument) == -1;
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
                               Tools.Combine(other.MuteInstruments, MuteInstruments),
                               other.MuteSamples || MuteSamples,
                               other.PreserveRate || PreserveRate,
                               other.RemoveRelease || RemoveRelease,
                               other.RemoveOff || RemoveOff,
                               other.NoOptimize || NoOptimize,
                               other.DumpNotes || DumpNotes,
                               other.DumpUncompressed || DumpUncompressed,
                               other.SampleRateCoeff * SampleRateCoeff,
                               other.VolumeCoeff * VolumeCoeff,
                               other.VolumeMode ?? VolumeMode);
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
            MuteInstruments = Tools.ParseMultiArg(args, muteInstrumentLongRegex, muteInstrumentShortRegex, Tools.ConvertASMValue);
            PreserveRate = Exists("--preserverate") || Exists("-pr");
            RemoveRelease = Exists("--removerelease") || Exists("-rr");
            RemoveOff = Exists("--removeoff") || Exists("-ro");
            NoOptimize = Exists("--nooptimize") || Exists("-no");
            DumpNotes = Exists("--dumpnotes") || Exists("-dn");
            DumpUncompressed = Exists("--dumpuncompressed") || Exists("-du");
            SampleRateCoeff = Tools.ParseFloatArg(args, sampleRateCoeffLongRegex, sampleRateCoeffShortRegex, 1f);
            VolumeCoeff = Tools.ParseFloatArg(args, volumeCoeffLongRegex, volumeCoeffShortRegex, 1f);
            if (Exists("--volume:linear") || Exists("-v:l")) VolumeMode = "linear";
            else if (Exists("--volume:nearest") || Exists("-v:n")) VolumeMode = "nearest";
        }

        private Options(int mute, int isolate, int[] muteInstruments, bool muteSamples, bool preserveRate, bool removeRelease, bool removeOff, bool noOptimize, bool dumpNotes, bool dumpUncompressed, float sampleRateCoeff, float volumeCoeff, string volumeMode)
        {
            Mute = mute;
            Isolate = isolate;
            MuteSamples = muteSamples;
            MuteInstruments = muteInstruments;
            PreserveRate = preserveRate;
            RemoveRelease = removeRelease;
            RemoveOff = removeOff;
            NoOptimize = noOptimize;
            DumpNotes = dumpNotes;
            DumpUncompressed = dumpUncompressed;
            SampleRateCoeff = sampleRateCoeff;
            VolumeCoeff = volumeCoeff;
            VolumeMode = volumeMode;
        }

        /// <summary>
        /// The default options.
        /// </summary>
        public static readonly Options Default = new Options(0, 0, new int[0], false, false, false, false, false, false, false, 1f, 1f, null);
    }
}