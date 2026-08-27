using System;

namespace SF2MusicCooker
{
    public sealed class Arguments
    {
        public readonly string Path;
        public readonly bool IncludeOriginalNames;
        public readonly bool NukeMusic;
        public readonly bool NukeSFX;
        public readonly bool NoPause;
        public readonly bool NoPostBuild;
        public readonly bool AutoYes;
        public readonly bool AutoNo;
        public readonly bool Test;
        public readonly int Only = -1;
        public readonly Options Options;

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
            Options = Options.Default;
        }

        public Arguments(string[] args)
        {
            bool Exists(string x) => Array.Exists(args, arg => arg.Equals(x, StringComparison.OrdinalIgnoreCase));
            Path = args.Length >= 1 ? args[0] : null;
            IncludeOriginalNames = Exists("--includeoriginalnames") || Exists("-ion");
            NukeMusic = Exists("--nukemusic") || Exists("-nm") || Exists("--nukeall") || Exists("-na");
            NukeSFX = Exists("--nukesfx") || Exists("-ns") || Exists("--nukeall") || Exists("-na");
            NoPause = Exists("--nopause") || Exists("-np");
            NoPostBuild = Exists("--nopostbuild") || Exists("-npb");
            AutoYes = Exists("--autoyes") || Exists("-ay");
            AutoNo = Exists("--autono") || Exists("-an");
            Test = Exists("--test") || Exists("-t");
            for (int i = 1; i < SFX.NONE; i++) if (Exists("--only" + i) || Exists("-o" + i)) Only = i;
            Options = new Options(args);
        }
    }
}