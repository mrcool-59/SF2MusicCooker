using System;
using System.Text.RegularExpressions;

namespace SF2MusicCooker
{
    public sealed class Arguments
    {
        private static readonly Regex inputLongRegex = new Regex("^--input=(.+)$");
        private static readonly Regex inputShortRegex = new Regex("^-i=(.+)$");

        public readonly string Path;
        public readonly string InputFolder;
        public readonly bool IncludeOriginalNames;
        public readonly bool NukeMusic;
        public readonly bool NukeSFX;
        public readonly bool NoPause;
        public readonly bool NoPostBuild;
        public readonly bool AutoYes;
        public readonly bool AutoNo;
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
            InputFolder = Tools.ParseStringArg(args, inputLongRegex, inputShortRegex);
            IncludeOriginalNames = Exists("--includeoriginalnames") || Exists("-ion");
            NukeMusic = Exists("--nukemusic") || Exists("-nm") || Exists("--nukeall") || Exists("-na");
            NukeSFX = Exists("--nukesfx") || Exists("-ns") || Exists("--nukeall") || Exists("-na");
            NoPause = Exists("--nopause") || Exists("-np");
            NoPostBuild = Exists("--nopostbuild") || Exists("-npb");
            AutoYes = Exists("--autoyes") || Exists("-ay");
            AutoNo = Exists("--autono") || Exists("-an");
            for (int i = 1; i < SFX.NONE; i++) if (Exists("--only" + i) || Exists("-o" + i)) Only = i;
            Options = new Options(args);
        }
    }
}