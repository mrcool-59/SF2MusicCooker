using SF2MusicCooker.Furnace;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SF2MusicCooker
{
    static class Program
    {
        static void Main(string[] args)
        {
            // Options
            bool Test(string x) => Array.Exists(args, arg => arg.Equals(x, StringComparison.OrdinalIgnoreCase));
            bool noPause = Test("--nopause") || Test("-np");
            bool noPostBuild = Test("--nopostbuild") || Test("-npb");
            bool noOptimizeNotes = Test("--nooptimize") || Test("-no");
            bool includeOriginalNames = Test("--includeoriginalnames") || Test("-ion");
            bool autoYes = Test("--autoyes") || Test("-ay");
            bool autoNo = Test("--autono") || Test("-an");
            bool dumpUncompressed = Test("--dumpuncompressed") || Test("-du");
            bool dumpNotes = Test("--dumpnotes") || Test("-dn");
            int isolateChannel = -1;
            for (int i = 0; i < 9; i++) if (Test("--channel" + i) || Test("-c" + i)) isolateChannel = i;

            // Arg to pass to postbuild script, if it needs to run
            string postBuild = null;

            try
            {
                if (args.Length == 0) throw new NotSupportedException("This program requires the path to the SF2DISASM folder as 1st argument.");

                string rootFolder = Path.GetFullPath(args[0]);

                Console.WriteLine("Path to SF2DISASM: {0}", rootFolder);

                Console.WriteLine("Checking support for 'expanded musics' feature...");
                Output.VerifySupport(rootFolder, out bool isFeatureSupported, out bool hasExtraBanks);
                if (!isFeatureSupported) throw new NotSupportedException("You are attempting to use this tool in a SF2DISASM folder that doesn't support expanded musics."
                    + Environment.NewLine + "Please merge 'feature/expanded_musics' branch into your project and try again!");
                Console.WriteLine("> Feature is supported! This tool may proceed.");
                Console.WriteLine("> Expanded music banks are {0}", hasExtraBanks ? "ENABLED" : "DISABLED");

                Console.WriteLine("Loading vanilla music data (numbers, names, sheets, FM instruments)...");
                Output output = Output.CreateForSF2DISASM(hasExtraBanks);
                output.LoadVanilla(rootFolder);
                Console.WriteLine("Loaded vanilla music data successfully!");

                FMInstruments instruments = output.Instruments;
                FileInfo[] furs = new DirectoryInfo("Input").GetFiles("*.fur");
                FileInfo[] activeFurs = Array.FindAll(furs, fur => !fur.Name.StartsWith("!"));
                HashSet<int> provided = new HashSet<int>();

                foreach (FileInfo fur in activeFurs)
                {
                    Tools.ExtractNumberAndName(fur.Name, out int number, out _, out _);

                    if (!provided.Add(number)) throw new InvalidOperationException("Cannot provide multiple .fur files for music " + number);
                }

                foreach (FileInfo fur in activeFurs)
                {
                    Tools.ExtractNumberAndName(fur.Name, out int number, out int moveFrom, out string name);

                    using (FileStream stream = fur.OpenRead())
                    {
                        Console.WriteLine("Reading '{0}' input file...", fur.Name);

                        // We first need to understand the contents of the .fur file
                        FurnaceFile file = FurnaceFile.ProbeUncompressed(stream) ? FurnaceFile.Load(stream) : FurnaceFile.LoadCompressed(stream, dumpUncompressed ? ("UNCOMPRESSED_" + fur.Name) : null);

                        // Verify A-4 tuning value
                        if (file.A4Tuning != FurnaceFile.StandardA4Tuning) Console.WriteLine("! This tool doesn't support A-4 tuning different from {0} hz, end result will sound incorrect if you proceed", FurnaceFile.StandardA4Tuning);
                        
                        // Remove notes we don't support in the note bible
                        int removed = file.RemoveUnsupportedNotes();
                        if (removed > 0) Console.WriteLine("! Removed {0} unsupported notes (notes must be between {1} and {2})", removed, NoteBible.FirstSupportedNote.Name, NoteBible.LastSupportedNote.Name);

                        // Warn the user of unsupported effects the .fur file may have
                        AsmSheetWriter.PrintUnsupportedEffects(file);

                        // Warn the user of unsupported sample maps the .fur file may have
                        AsmSheetWriter.PrintUnsupportedSampleMaps(file);

                        // Complete the global FM instruments by those present in this .fur file
                        instruments.AddMany(file, dumpNotes);

                        // Some musics come in pairs in vanilla SF2, we need to carefully handle those to not break the reassembly when replacing these musics
                        int pairNumber = output.GetPairedMusic(number);
                        if (provided.Contains(pairNumber)) pairNumber = 0; // Both elements of the pair have been provided, no need to use this hack

                        // Prepare the options
                        Options options = new Options("[CUSTOM] " + name, null, isolateChannel, !noOptimizeNotes, dumpNotes);

                        // Write the ASM sheet of the music
                        string sheet = AsmSheetWriter.Write(file, options, instruments, number, pairNumber);

                        // Build ASM name
                        string asmName = "MUSIC_CUSTOM_" + Tools.GetASMValidName(name);

                        // Send to output!
                        Song song = new Song(number, name, asmName, sheet);
                        if (moveFrom != 0)
                        {
                            if (moveFrom == number)
                            {
                                // Replace
                                output.Replace(song, includeOriginalNames);
                                if (pairNumber > 0)
                                {
                                    output.Replace(new Song(pairNumber, name, asmName, null), includeOriginalNames);
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
                                    output.MoveReplace(song, moveFrom, includeOriginalNames);
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

                if (activeFurs.Length == 0)
                {
                    Console.WriteLine("WARNING: No .fur file found in input folder! The tool can still proceed anyway...");
                }

                output.PadLast();

                // TODO: implement here SFX add/replace feature

                Console.WriteLine("Bank storage summary:");
                bool overloaded = output.PrintSize();
                if (overloaded)
                {
                    if (autoYes || autoNo)
                    {
                        autoYes = false;
                        autoNo = false;
                        Console.WriteLine("! Auto Y/N has been turned off because one of the Banks is overloaded and requires manual review.");
                    }
                    Console.WriteLine("WARNING: At least 1 Bank is overloaded, assembling the ROM will probably fail!");
                    Console.WriteLine("         For musics: you should move some musics from that Music Bank to Music Banks that still have remaining space.");
                    if (!hasExtraBanks)
                    {
                        Console.WriteLine("         /!\\ We highly recommand you enable 'EXPANDED_MUSIC_BANKS' feature in 'sf2patches.asm' to solve this issue. /!\\");
                    }
                    Console.WriteLine("         For SFXs: sorry, you have no other option but to sacrifice some of your SFXs to make room.");
                }

                bool writeToSF2DISASM = autoYes && !autoNo;
                if (!autoYes && !autoNo)
                {
                    Console.WriteLine("The tool is about to write output files (Y/N).");
                    Console.WriteLine("* Press 'Y' to write output files directly to the appropriate locations in SF2DISASM folder.");
                    Console.WriteLine("* Press 'N' to write to 'Output' folder instead (any existing 'Output' folder will be deleted beforehand).");

                    while (true)
                    {
                        ConsoleKeyInfo answer = Console.ReadKey(true);
                        writeToSF2DISASM = answer.Key == ConsoleKey.Y;
                        if (answer.Key == ConsoleKey.Y || answer.Key == ConsoleKey.N) break;
                    }
                }

                if (writeToSF2DISASM)
                {
                    Console.WriteLine("Writing to SF2DISASM...");
                    output.WriteToSF2DISASM(rootFolder);
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

            if (!noPause)
            {
                Console.WriteLine("-- Press a key to exit --");
                _ = Console.ReadKey(true);
            }

            string path = Path.GetFullPath("POSTBUILD.bat");
            if (postBuild != null && !noPostBuild && File.Exists(path))
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
    }
}