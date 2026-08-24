using System;

namespace SF2MusicCooker
{
    public sealed class Arguments
    {
        public readonly string Path;
        public readonly bool NoPause;
        public readonly bool NoPostBuild;
        public readonly bool NoOptimizeNotes;
        public readonly bool IncludeOriginalNames;
        public readonly bool NukeMusic;
        public readonly bool NukeSFX;
        public readonly bool AutoYes;
        public readonly bool AutoNo;
        public readonly bool DumpUncompressed;
        public readonly bool DumpNotes;
        public readonly bool Test;
        public readonly int IsolateChannel = -1;
        public readonly int Only = -1;

        /// <summary>
        /// Throws an exception if a mandatory argument is missing or invalid.
        /// </summary>
        public void ThrowIfInvalid()
        {
            if (Path == null)
                throw new InvalidOperationException("This program requires the path to the SF2DISASM folder as 1st argument.");
        }

        public Arguments(string path)
        {
            Path = path;
        }

        public Arguments(string[] args)
        {
            bool Exists(string x) => Array.Exists(args, arg => arg.Equals(x, StringComparison.OrdinalIgnoreCase));
            Path = args.Length >= 1 ? args[0] : null;
            NoPause = Exists("--nopause") || Exists("-np");
            NoPostBuild = Exists("--nopostbuild") || Exists("-npb");
            NoOptimizeNotes = Exists("--nooptimize") || Exists("-no");
            IncludeOriginalNames = Exists("--includeoriginalnames") || Exists("-ion");
            NukeMusic = Exists("--nukemusic") || Exists("-nm");
            NukeSFX = Exists("--nukesfx") || Exists("-ns");
            AutoYes = Exists("--autoyes") || Exists("-ay");
            AutoNo = Exists("--autono") || Exists("-an");
            DumpUncompressed = Exists("--dumpuncompressed") || Exists("-du");
            DumpNotes = Exists("--dumpnotes") || Exists("-dn");
            Test = Exists("--test") || Exists("-t");
            for (int i = 0; i < 9; i++) if (Exists("--channel" + i) || Exists("-c" + i)) IsolateChannel = i;
            for (int i = 1; i < SFX.NONE; i++) if (Exists("--only" + i) || Exists("-o" + i)) Only = i;
        }
    }
}