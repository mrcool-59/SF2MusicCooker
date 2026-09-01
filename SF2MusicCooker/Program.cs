using SF2MusicCooker.Furnace;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace SF2MusicCooker
{
    static class Program
    {
        static void Main(string[] args)
        {
            Arguments arguments = new Arguments(args);
            string postBuild = null; // Argument to pass to postbuild script, if it needs to run
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                arguments.ThrowIfInvalid();
                string rootFolder = Path.GetFullPath(arguments.Path);
                Console.WriteLine("Path to SF2DISASM: {0}", rootFolder);
                Output output = Output.CreateForSF2DISASM(rootFolder);
                Console.WriteLine("Loading vanilla music data (numbers, names, sheets, FM instruments, PCM samples)...");
                output.LoadVanilla();

                List<Sheet> sheets = new List<Sheet>();
                string folder = arguments.InputFolder ?? "Input";
                FileInfo[] musics = Tools.GetActiveFiles(folder, "*music*.fur");
                FileInfo[] sfxs = Tools.GetActiveFiles(folder, "*sfx*.fur");
                Dictionary<int, Options> overrides = Options.ReadOverrideOptions(folder);

                if (musics.Length == 0 && sfxs.Length == 0)
                {
                    Console.WriteLine("WARNING: No recognized input .fur file found! The tool can still proceed anyway...");
                }
                else
                {
                    DoMusics(sheets, musics, output, arguments, overrides);
                    DoSFXs(sheets, sfxs, output, arguments, overrides);
                }

                // Nuke vanilla
                output.NukeVanilla(arguments.NukeMusic, arguments.NukeSFX);

                // Pad the music banks
                output.PadLast();

                // Remove unused assets
                output.RemoveUnusedAssets(true);

                // Build sheets that have been deferred
                foreach (Sheet sheet in sheets) sheet.Build();

                // Verify SFXs are in a valid state (should always be successful)
                output.VerifyAndUpdateSFXDependencies();

                // Show time amount used to build musics
                Console.WriteLine("-- TOTAL BUILD TIME: {0} sec --", stopwatch.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture));

                Console.WriteLine("Bank storage summary:");
                bool overloaded = output.PrintSize();
                bool autoYes = arguments.AutoYes;
                bool autoNo = arguments.AutoNo;
                if (overloaded)
                {
                    if (autoYes || autoNo)
                    {
                        autoYes = false;
                        autoNo = false;
                        Console.WriteLine("! Auto Y/N has been turned off because one of the Banks is overloaded and requires manual review.");
                    }
                    Console.WriteLine("WARNING: At least 1 Bank is overloaded, assembling the ROM will either fail or produce broken results!");
                    Console.WriteLine("         For musics: you should move some musics from that Music Bank to Music Banks that still have remaining space.");
                    if (output.Name == "SF2DISASM" && !output.HasExtBanks)
                    {
                        Console.WriteLine("         /!\\ We highly recommand you enable 'EXPANDED_MUSIC_BANKS' feature in 'sf2patches.asm' to solve this issue. /!\\");
                    }
                    Console.WriteLine("         For SFXs: sorry, you have no other option but to sacrifice some of your SFXs to make room.");
                }

                bool writeToDISASM = autoYes && !autoNo;
                if (!autoYes && !autoNo)
                {
                    Console.WriteLine("The tool is about to write output files (Y/N).");
                    Console.WriteLine("* Press 'Y' to write output files directly to the appropriate locations in {0} folder.", output.Name);
                    Console.WriteLine("* Press 'N' to write to 'Output' folder instead (any existing 'Output' folder will be deleted beforehand).");
                    WaitForYesNo(ref writeToDISASM);

                    if (overloaded && writeToDISASM)
                    {
                        Console.WriteLine("Are you really sure? The ROM *will* be broken! (Y/N)");
                        WaitForYesNo(ref writeToDISASM);
                    }
                }

                if (writeToDISASM)
                {
                    Console.WriteLine("Writing to {0}...", output.Name);
                    output.WriteToDISASM();
                    postBuild = "\"" + rootFolder + "\"";
                }
                else
                {
                    Console.WriteLine("Writing output files...");
                    output.WriteToFolder("Output");
                    postBuild = null;
                }
                Console.WriteLine("SUCCESS");
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: {0}", Tools.Unwrap(ex));
                Environment.ExitCode = 1;
            }

            if (!arguments.NoPause)
            {
                Console.WriteLine("-- Press a key to exit --");
                _ = Console.ReadKey(true);
            }

            string path = Path.GetFullPath("POSTBUILD.bat");
            if (postBuild != null && !arguments.NoPostBuild && File.Exists(path))
            {
                Process.Start(new ProcessStartInfo()
                {
                    UseShellExecute = true,
                    FileName = path,
                    WorkingDirectory = Path.GetDirectoryName(path),
                    Arguments = postBuild
                });
            }
        }

        static void WaitForYesNo(ref bool answer)
        {
            while (true)
            {
                ConsoleKeyInfo info = Console.ReadKey(true);
                answer = info.Key == ConsoleKey.Y;
                if (info.Key == ConsoleKey.Y || info.Key == ConsoleKey.N) break;
            }
        }

        static void DoMusics(List<Sheet> sheets, FileInfo[] furs, Output output, Arguments arguments, Dictionary<int, Options> overrides)
        {
            HashSet<int> provided = new HashSet<int>();

            foreach (FileInfo fur in furs)
            {
                Tools.ExtractNumberAndName(fur.Name, true, out int number, out _, out _);

                if (!provided.Add(number)) throw new InvalidOperationException("Cannot provide multiple .fur files for music " + number);
            }

            foreach (FileInfo fur in furs)
            {
                Tools.ExtractNumberAndName(fur.Name, true, out int number, out int moveFrom, out string name);

                if (arguments.Only >= 0 && number != arguments.Only) continue;

                // Some musics come in pairs in vanilla SF2, we need to carefully handle those to not break the reassembly when replacing these musics
                int pairNumber = output.GetPairedMusic(number);
                if (provided.Contains(pairNumber)) pairNumber = 0; // Both elements of the pair have been provided, no need to use this hack

                // Prepare the options
                Options options = Options.GetFinalOptions(number, arguments.Options, overrides);

                // Generate the builder
                Func<string> builder = Builder(fur, number, pairNumber, null, output.Instruments, output.Samples, output.Pitch, options, name);

                // Build ASM name
                string asmName = "MUSIC_CUSTOM_" + Tools.GetASMValidName(name);

                // Create a deferred sheet
                Sheet sheet = Sheet.Later(builder);
                sheets.Add(sheet);

                // Send to output!
                Song song = new Song(number, name, asmName, sheet);
                if (moveFrom != 0)
                {
                    if (moveFrom == number)
                    {
                        // Replace
                        output.Replace(song, arguments.IncludeOriginalNames);
                        if (pairNumber > 0)
                        {
                            output.Replace(new Song(pairNumber, name, asmName, Sheet.Clone), arguments.IncludeOriginalNames);
                            Console.WriteLine("WARNING: music {0} will also be replaced by music {1} since they are linked in pairs", pairNumber, number);
                        }
                    }
                    else
                    {
                        // Move-replace
                        if (pairNumber > 0)
                        {
                            throw new NotSupportedException("Sorry, MOVE-REPLACE cannot be used on music " + number + " because it is paired with music " + pairNumber + Environment.NewLine
                                + "Please use REPLACE instead for these musics (keep in mind you can MOVE-REPLACE other musics to make room in Music Bank 0 if needed)");
                        }
                        else
                        {
                            output.MoveReplace(song, moveFrom, arguments.IncludeOriginalNames);
                        }
                    }
                }
                else
                {
                    // Add
                    output.Add(song);
                }
            }
        }

        static void DoSFXs(List<Sheet> sheets, FileInfo[] furs, Output output, Arguments arguments, Dictionary<int, Options> overrides)
        {
            List<SFX> sfxs = new List<SFX>(furs.Length);

            foreach (FileInfo fur in furs)
            {
                Tools.ExtractNumberAndName(fur.Name, false, out int number, out int moveFrom, out string name);

                if (arguments.Only >= 0 && number != arguments.Only) continue;

                // Prepare the options
                Options options = Options.GetFinalOptions(number, arguments.Options, overrides);

                // Pick a pointer name
                string pointerName = "CSFX_" + sfxs.Count;

                // Generate the builder
                Func<string> builder = Builder(fur, number, 0, pointerName, output.Instruments, output.Samples, output.Pitch, options, name);

                // Build ASM name
                string asmName = "SFX_CUSTOM_" + Tools.GetASMValidName(name);

                // Create a deferred sheet
                Sheet sheet = Sheet.Later(builder);
                sheets.Add(sheet);

                // Done!
                sfxs.Add(new SFX(number, name, asmName, pointerName, sheet));
            }

            // Send to output!
            output.AddOrReplaceSFX(sfxs.ToArray(), arguments.IncludeOriginalNames);
        }

        static Func<string> Builder(FileInfo fur, int number, int pairNumber, string pointerName, FMInstruments instruments, PCMInstruments samples, PitchTable pitch, Options options, string name)
        {
            return () =>
            {
                using (FileStream stream = fur.OpenRead())
                {
                    Console.WriteLine("Reading '{0}' input file...", fur.Name);

                    // We first need to understand the contents of the .fur file
                    FurnaceFile file = FurnaceFile.ProbeUncompressed(stream) ? FurnaceFile.Load(stream) : FurnaceFile.LoadCompressed(stream, options.DumpUncompressed ? Path.Combine("Uncompressed", fur.Name) : null);

                    // We can't work with extended channel 3, but we can cut the channels back to standard 10
                    FurnaceFile newFile = file.DropExtended();
                    if (newFile != file)
                    {
                        file = newFile;
                        Console.WriteLine("! YM2612 is in Extended Channel 3 mode, it is unsupported and the extra channels will be ignored");
                    }

                    // Remove notes we don't support in the note bible
                    int removed = file.RemoveUnsupportedNotes();
                    if (removed > 0) Console.WriteLine("! Removed {0} unsupported notes (notes must be between {1} and {2})", removed, NoteBible.FirstSupportedNote.Name, NoteBible.LastSupportedNote.Name);

                    // Remove === and OFF notes if the option is enabled
                    if (options.RemoveRelease)
                    {
                        removed = file.RemoveNotes(PatternCell.NoteRelease);
                        if (removed > 0) Console.WriteLine("> Removed {0} note release commands (===)", removed);
                    }
                    if (options.RemoveOff)
                    {
                        removed = file.RemoveNotes(PatternCell.NoteOff);
                        if (removed > 0) Console.WriteLine("> Removed {0} note off commands (OFF)", removed);
                    }

                    // Identify the instruments that are really used and verify their usage is valid
                    Instrument[] usedInstruments = file.GetUsedInstruments(true, true, true);
                    int unused = file.Instruments.Length - usedInstruments.Length;
                    if (unused > 0) Console.WriteLine("> This file has {0} unused instruments", unused);

                    // Sample rate coeff for samples
                    file.ScaleSamples(options.SampleRateCoeff);

                    // Mute samples switch
                    int numSamples = file.Samples.Length;
                    if (options.MuteSamples && numSamples > 0)
                    {
                        file.MuteSamples();
                        Console.WriteLine("> Muted {0} samples (--mutesamples)", numSamples);
                    }

                    // Adjust the playback rate to play nice with YM2612 timer and SFXs play speed
                    AsmSheetWriter.AdjustPlayRate(ref file, pointerName != null, !options.PreserveRate);

                    // Warn the user of unsupported effects the .fur file may have
                    AsmSheetWriter.PrintUnsupportedEffects(file);

                    // Complete the global FM instruments by those present in this .fur file, if they are really used
                    instruments.AddMany(usedInstruments, options.DumpNotes);

                    // Complete the global samples by those present in this .fur file, if they are really used
                    samples.AddMany(file, usedInstruments, options.DumpNotes);

                    // Prepare the Furnace to Cube instrument map
                    InstrumentMap map = new InstrumentMap(instruments, samples, file, usedInstruments);

                    // Prepare the title
                    string title = "[CUSTOM] " + name;

                    // Write the ASM sheet of the music/SFX
                    if (pointerName != null)
                        return AsmSheetWriter.WriteSFX(file, options, map, pitch, pointerName, SFXType.Automatic, title);
                    else
                        return AsmSheetWriter.Write(file, options, map, pitch, number, pairNumber, title);
                }
            };
        }
    }
}