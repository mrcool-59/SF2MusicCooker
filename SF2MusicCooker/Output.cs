using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SF2MusicCooker
{
    public sealed class Output
    {
        private readonly FilePaths _paths;
        private readonly Bank[] _banks;
        private readonly BankSFX[] _sfxBanks;
        private readonly int[] _musicPairs;
        private readonly string _soundTestTemplate;

        private Dictionary<int, HashSet<int>> _sfxNeededByMap;

        /// <summary>
        /// Name of the DISASM.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The global list of FM instruments (for Channel 1-5 + Channel 6 in FM mode).
        /// </summary>
        public FMInstruments Instruments { get; }

        /// <summary>
        /// The global list of PCM samples (for Channel 6 in DAC mode).
        /// </summary>
        public PCMInstruments Samples { get; }

        /// <summary>
        /// The pitch table to map Furnace notes to Cube notes.
        /// </summary>
        public PitchTable Pitch { get; }

        /// <summary>
        /// True if ext banks are enabled.
        /// </summary>
        public bool HasExtBanks { get { return Array.Exists(_banks, bank => bank.Name.Contains("musicbankext")); } }

        public Output(string name, FilePaths paths, Bank[] banks, BankSFX[] sfxBanks, int[] pcmBanks, string[] pcmNames, int pcmBaseOffset, int pcmSlots = PCMInstruments.MAX_SLOTS, int instrumentSlots = FMInstruments.MAX_SLOTS, int[] musicPairs = null, string soundTestTemplate = null)
        {
            if (pcmBanks == null)
                throw new ArgumentNullException(nameof(pcmBanks));

            if (pcmNames == null)
                throw new ArgumentNullException(nameof(pcmNames));

            if (musicPairs != null && musicPairs.Length % 2 != 0)
                throw new ArgumentException("must contain an even number of elements (to form pairs)", nameof(musicPairs));

            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _banks = banks ?? throw new ArgumentNullException(nameof(banks));
            _sfxBanks = sfxBanks ?? throw new ArgumentNullException(nameof(sfxBanks));
            _musicPairs = musicPairs;
            _soundTestTemplate = soundTestTemplate;

            _sfxNeededByMap = null; // Initial state: no dependencies exist

            Name = name ?? throw new ArgumentNullException(nameof(name));

            Instruments = new FMInstruments(instrumentSlots);

            Samples = new PCMInstruments(pcmSlots, pcmBanks, pcmNames, pcmBaseOffset);

            Pitch = new PitchTable(paths.YmFrequencies, paths.NoteNames);
        }

        /// <summary>
        /// Create Output tailored for Shining Force 2 with SF2DISASM.
        /// </summary>
        public static Output CreateForSF2DISASM(string rootFolder)
        {
            const string name = "SF2DISASM";
            string patches = Path.Combine(rootFolder, "disasm\\sf2patches.asm");

            Console.WriteLine("Checking support for 'expanded musics' feature...");
            bool hasExtBanks = VerifyPatch(patches, name, "EXPANDED_MUSIC_BANKS", "feature/expanded_musics");
            Console.WriteLine("> Feature is supported! This tool may proceed.");
            Console.WriteLine("> Expanded music banks are {0}", hasExtBanks ? "ENABLED" : "DISABLED");

            FilePaths paths = new FilePaths(rootFolder);

            Bank[] banks;
            int[] pcmBanks;
            string[] pcmNames;
            int pcmSlots;
            int instrumentSlots;

            if (hasExtBanks)
            {
                banks = new Bank[4]
                {
                    new Bank("musicbank0", 0x8000, 1, 32),
                    new Bank("musicbank1", 0x8000, 33, 16), // Shrinked compared to vanilla
                    new Bank("musicbankext0", 0x8000, 49, 8), // Extra bank 1
                    new Bank("musicbankext1", 0x8000, 57, 8) // Extra bank 2
                };

                pcmBanks = new int[4]
                {
                    0x8000, // PCM bank 1
                    0x3000, // PCM bank 2
                    0x8000, // Extra bank 1
                    0x8000, // Extra bank 2
                };

                pcmNames = new string[4]
                {
                    "pcmbank0",
                    "pcmbank1",
                    "pcmbankext0",
                    "pcmbankext1",
                };

                pcmSlots = PCMInstruments.MAX_SLOTS;

                instrumentSlots = FMInstruments.MAX_SLOTS;
            }
            else
            {
                banks = new Bank[2]
                {
                    new Bank("musicbank0", 0x8000, 1, 32),
                    new Bank("musicbank1", 0x8000, 33, 32)
                };

                pcmBanks = new int[2]
                {
                    0x8000, // PCM bank 1
                    0x3000, // PCM bank 2
                };

                pcmNames = new string[2]
                {
                    "pcmbank0",
                    "pcmbank1",
                };

                pcmSlots = 17; // FIXME: maybe it's possible to increase it a little without crashing

                instrumentSlots = 80; // FIXME: maybe it's possible to increase it to the theorical max without issue
            }

            BankSFX[] sfxBanks = new BankSFX[1]
            {
                // Quick calculation of available size for SFX bank: 64k - 44k (PCM bank 0-1) - 4k (FM instruments) - 8k (sound driver) = 8k remaining
                new BankSFX("sfxbank", 0x2000)
            };

            int[] musicPairs = new int[] { 3, 4, 13, 14 };

            return new Output(name, paths, banks, sfxBanks, pcmBanks, pcmNames, 0x8000, pcmSlots, instrumentSlots, musicPairs, "soundtest-standard.asm.tpl");
        }

        private Bank SelectMusicBank(int number)
        {
            foreach (Bank bank in _banks)
            {
                if (bank.InRange(number))
                    return bank;
            }

            throw new NotSupportedException("Unable to find the appropriate music bank for music number " + number);
        }

        private SFX FindSFXAndBank(int number, out BankSFX bank)
        {
            foreach (BankSFX b in _sfxBanks)
            {
                SFX sfx = b.Find(number, out _);
                if (sfx != null)
                {
                    bank = b;
                    return sfx; // SFX found
                }
            }

            bank = Tools.SelectMax(_sfxBanks, b => b.MaxSize - b.GetEstimatedSize()); // Overkill as there is usually only 1 SFX bank
            if (bank == null) throw new NotSupportedException("No SFX bank is present!");
            return null; // SFX not found (but we have a bank)
        }

        private Song[] GetAllSongs(bool putCustomFirst)
        {
            List<Song> allSongs = new List<Song>();
            if (putCustomFirst) foreach (Bank bank in _banks) allSongs.AddRange(bank.Custom);
            foreach (Bank bank in _banks) allSongs.AddRange(bank.Vanilla);
            if (!putCustomFirst) foreach (Bank bank in _banks) allSongs.AddRange(bank.Custom);
            return allSongs.ToArray();
        }

        private SFX[] GetAllSFXs(bool putCustomFirst)
        {
            List<SFX> allSFXs = new List<SFX>();
            if (putCustomFirst) foreach (BankSFX bank in _sfxBanks) allSFXs.AddRange(bank.Custom);
            foreach (BankSFX bank in _sfxBanks) allSFXs.AddRange(bank.Vanilla);
            if (!putCustomFirst) foreach (BankSFX bank in _sfxBanks) allSFXs.AddRange(bank.Custom);
            return allSFXs.ToArray();
        }

        /// <summary>
        /// Load vanilla music data from provided paths (outside of SF2, this method is able to read data for other Cube games with similar input files).
        /// </summary>
        public void LoadVanilla()
        {
            Dictionary<int, string> number2name = _paths.MusicNamesTxt != null ? Tools.ReadNumberStringMap(_paths.MusicNamesTxt) : new Dictionary<int, string>(); // Load music names (for sound test)
            Dictionary<int, string> number2enum = Tools.ReadASMEnumReverseMap(_paths.MusicNumbersAndAsmNames); // Load numbers and ASM names
            Dictionary<int, string> sfx_number2enum = Tools.ReadASMEnumReverseMap(_paths.SfxNumbersAndAsmNames); // Load numbers and ASM names for SFXs
            Regex regex = new Regex("^music([0-9]+)\\.asm$");
            Regex sfxPointerNameRegex = new Regex("dw[ \t]+(Sfx_[0-9]+)[ \t\r\n]");
            Regex sfxPointerNameLabelRegex = new Regex("(Sfx_[0-9]+):");

            // Load music sheets from music banks
            foreach (string folder in _paths.MusicBankFolders)
            {
                foreach (string filename in Directory.GetFiles(folder, "music*.asm", SearchOption.TopDirectoryOnly))
                {
                    Match match = regex.Match(Path.GetFileName(filename));
                    if (match.Success)
                    {
                        int number = int.Parse(match.Groups[1].Value);
                        number2name.TryGetValue(number, out string name);
                        number2enum.TryGetValue(number, out string asmName);
                        string asm = File.ReadAllText(filename);

                        // Don't add unreachable musics (i.e: those who don't have a defined ASM name)
                        // These pseudo-musics are probably used for adding sentinel values in case sound driver reads out of bounds data
                        // We are going to add our own sentinel music anyway
                        if (asmName == null) continue;

                        Song song = new Song(number, name, asmName, new Sheet(asm));
                        Bank bank = SelectMusicBank(song.Number);
                        bank.Add(song, true);

                        // Special case for music 4 and 14
                        int pairNumber = GetPairedMusic(number);
                        if (pairNumber > 0)
                        {
                            _ = number2name.TryGetValue(pairNumber, out string pairName);
                            _ = number2enum.TryGetValue(pairNumber, out string pairAsmName);
                            Song pairSong = new Song(pairNumber, pairName, pairAsmName, Sheet.Clone);

                            Bank pairBank = SelectMusicBank(pairNumber);
                            pairBank.Add(pairSong, true);
                        }
                    }
                }
            }

            // Load SFX banks (usually only 1)
            int currentBank = 0;
            int currentSfx = SFX.FIRST;
            foreach (string folder in _paths.SfxBankFolders)
            {
                string filename = Path.Combine(folder, Path.GetFileName(folder) + ".asm");
                string compositeAsm = File.ReadAllText(filename);
                string[] pointerNames = Tools.GetAllStringElements(compositeAsm, sfxPointerNameRegex);
                BankSFX bank = _sfxBanks[currentBank];

                foreach (string pointerName in pointerNames)
                {
                    int number = currentSfx;
                    sfx_number2enum.TryGetValue(number, out string asmName);

                    // Don't add unreachable SFXs (i.e: those who don't have a defined ASM name)
                    if (asmName == null) continue;

                    string asm = AsmSheetToolkit.SplitByLabel(compositeAsm, sfxPointerNameLabelRegex, pointerName);

                    SFX sfx = new SFX(number, null, asmName, pointerName, new Sheet(asm));
                    bank.Add(sfx, true);

                    currentSfx++;
                }

                currentBank++;
            }

            // Verify that all puzzle pieces fit for SFXs (because of their channel pointers that can reference data defined in other SFXs...)
            _sfxNeededByMap = AsmSheetToolkit.VerifyAndGetDependencies(_sfxBanks);

            // Load FM instruments
            Instruments.Load(_paths.YmInstBin);

            // Load PCM banks
            Samples.Load(_paths.PcmSamples, _paths.PcmBankFiles);
        }

        /// <summary>
        /// Replace vanilla musics/SFXs by emptiness.
        /// </summary>
        public void NukeVanilla(bool music, bool sfx)
        {
            if (music)
            {
                foreach (Bank bank in _banks) bank.Nuke();
                Console.WriteLine("> Nuked vanilla musics!");
            }

            if (sfx)
            {
                foreach (BankSFX bank in _sfxBanks) bank.Nuke();
                Console.WriteLine("> Nuked vanilla SFXs!");
            }
        }

        /// <summary>
        /// Remove unused FM instruments and samples by examining music and SFX sheets. Sheets that haven't been built yet are ignored.
        /// </summary>
        public void RemoveUnusedAssets(bool print)
        {
            HashSet<int> usedInstruments = new HashSet<int>();
            HashSet<int> usedSamples = new HashSet<int>();

            void Process(Sheet sheet)
            {
                if (sheet.Built)
                {
                    AsmSheetToolkit.FillInstruments(sheet, usedInstruments);
                    AsmSheetToolkit.FillSamples(sheet, usedSamples);
                }
            }

            foreach (Song song in GetAllSongs(false)) Process(song.Sheet);
            foreach (SFX sfx in GetAllSFXs(false)) Process(sfx.Sheet);

            int removedInstruments = Instruments.ClearExcept(usedInstruments);
            int removedSamples = Samples.ClearExcept(usedSamples);

            if (removedInstruments > 0 && print)
                Console.WriteLine("> Removed {0} unused FM instrument{1}", removedInstruments, removedInstruments > 1 ? "s" : "");

            if (removedSamples > 0 && print)
                Console.WriteLine("> Removed {0} unused sample{1}", removedSamples, removedSamples > 1 ? "s" : "");
        }

        /// <summary>
        /// Add a custom song.
        /// </summary>
        public void Add(Song song)
        {
            Bank bank = SelectMusicBank(song.Number);
            bank.Add(song);
        }

        /// <summary>
        /// Replace a song.
        /// </summary>
        public void Replace(Song song, bool includeOriginalName = false)
        {
            Bank bank = SelectMusicBank(song.Number);
            Song originalSong = bank.Remove(song.Number, out _);

            song = song.UpdateASMName(originalSong.ASMName);
            if (includeOriginalName) song = song.UpdateName(GetCombinedName(song.Name, originalSong.Name ?? originalSong.ASMName));
            bank.Add(song);
        }

        /// <summary>
        /// Move-replace a song.
        /// </summary>
        public void MoveReplace(Song song, int moveFromNumber, bool includeOriginalName = false)
        {
            Bank originalBank = SelectMusicBank(moveFromNumber);
            Song originalSong = originalBank.Remove(moveFromNumber, out _);

            Bank bank = SelectMusicBank(song.Number);
            song = song.UpdateASMName(originalSong.ASMName);
            if (includeOriginalName) song = song.UpdateName(GetCombinedName(song.Name, originalSong.Name ?? originalSong.ASMName));
            bank.Add(song);
        }

        /// <summary>
        /// Add custom SFXs or replace vanilla SFXs. All SFXs must be prepared in advance because this method will verify dependencies consistency.
        /// </summary>
        public void AddOrReplaceSFX(SFX[] sfxs, bool includeOriginalName = false)
        {
            // Dependencies verification
            VerifyReplaceSFXAllowed(new HashSet<int>(sfxs.Select(s => s.Number)));

            foreach (SFX sfx in sfxs)
            {
                SFX addedSfx = sfx;
                SFX originalSfx = FindSFXAndBank(sfx.Number, out BankSFX bank);

                // Is it a 'replace' operation? (instead of a 'add')
                if (originalSfx != null)
                {
                    addedSfx = addedSfx.UpdateASMName(originalSfx.ASMName);
                    if (includeOriginalName) addedSfx = addedSfx.UpdateName(GetCombinedName(sfx.Name, originalSfx.Name));
                    bank.Remove(originalSfx.Number, out _);
                }

                bank.Add(addedSfx);
            }

            // Update 'needed by' map
            _sfxNeededByMap = AsmSheetToolkit.VerifyAndGetDependencies(_sfxBanks);
        }

        private static string GetCombinedName(string name, string originalName)
        {
            if (originalName == null)
                return name;
            else
                return name + " / " + originalName;
        }

        /// <summary>
        /// Ensure the last music of all banks contains an empty sheet if we didn't provide anything.
        /// </summary>
        public void PadLast()
        {
            foreach (Bank bank in _banks)
            {
                _ = bank.Pad(bank.LastNumber);
            }
            // Note: this doesn't apply to SFX banks
        }

        /// <summary>
        /// Print size evaluation of each bank and return true if any bank is overloaded.
        /// </summary>
        public bool PrintSize()
        {
            bool anyOverloaded = false;
            foreach (Bank bank in _banks)
            {
                if (bank.PrintSize()) anyOverloaded = true;
            }
            foreach (BankSFX bank in _sfxBanks)
            {
                if (bank.PrintSize()) anyOverloaded = true;
            }
            Samples.PrintSize();
            return anyOverloaded;
        }

        /// <summary>
        /// Write generated files to a single output folder.
        /// </summary>
        public void WriteToFolder(string path)
        {
            DeleteAndCreateFolder(path);
            foreach (Bank bank in _banks)
            {
                string bankPath = Path.Combine(path, bank.FolderName);
                DeleteAndCreateFolder(bankPath);
                bank.Write(bankPath);
                bank.WriteSheets(bankPath);
            }
            foreach (BankSFX bank in _sfxBanks)
            {
                string bankPath = Path.Combine(path, bank.FolderName);
                DeleteAndCreateFolder(bankPath);
                bank.Write(bankPath);
            }
            WriteASMMusicEnum(path);
            WriteASMSfxEnum(path);
            WriteASMSoundTest(path);
            WriteFMInstruments(path);
            WriteSamples(path, path);
        }

        /// <summary>
        /// Write generated files to the correct locations in DISASM folder.
        /// </summary>
        public void WriteToDISASM()
        {
            string musicEnumFolder = Path.GetDirectoryName(_paths.MusicNumbersAndAsmNames);
            string sfxEnumFolder = Path.GetDirectoryName(_paths.SfxNumbersAndAsmNames);
            string ymInstFolder = Path.GetDirectoryName(_paths.YmInstBin);
            string musicBanksFolder = _banks.Length > 0 ? Path.GetDirectoryName(_paths.MusicBankFolders[0]) : null;
            string sfxBanksFolder = _sfxBanks.Length > 0 ? Path.GetDirectoryName(_paths.SfxBankFolders[0]) : null;
            string pcmSamplesFolder = Path.GetDirectoryName(_paths.PcmSamples);
            string pcmBanksFolder = Samples.NumBanks > 0 ? Path.GetDirectoryName(_paths.PcmBankFiles[0]) : null;

            foreach (Bank bank in _banks)
            {
                string bankPath = Path.Combine(musicBanksFolder, bank.FolderName);
                DeleteAndCreateFolder(bankPath);
                bank.Write(bankPath);
                bank.WriteSheets(bankPath);
            }
            foreach (BankSFX bank in _sfxBanks)
            {
                string bankPath = Path.Combine(sfxBanksFolder, bank.FolderName);
                DeleteAndCreateFolder(bankPath);
                bank.Write(bankPath);
            }
            WriteASMMusicEnum(musicEnumFolder);
            WriteASMSfxEnum(sfxEnumFolder);
            WriteASMSoundTest(_paths.SoundTestFolder);
            WriteFMInstruments(ymInstFolder);
            WriteSamples(pcmSamplesFolder, pcmBanksFolder);
        }

        private void DeleteAndCreateFolder(string path)
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch { }

            Directory.CreateDirectory(path);
        }

        private void WriteASMMusicEnum(string path)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendFormat("{0}: equ {1}", "MUSIC_NOTHING", 0);
            sb.AppendLine();

            foreach (Song song in GetAllSongs(false))
            {
                if (song.ASMName != null) // Filter out dummy padding songs used as sentinels
                {
                    sb.AppendFormat("{0}: equ {1}", song.ASMName, song.Number);
                    sb.AppendLine();
                }
            }

            File.WriteAllText(Path.Combine(path, "musics-standard.asm"), sb.ToString());
            Console.WriteLine("> Wrote 'musics-standard.asm' file!");
        }

        private void WriteASMSfxEnum(string path)
        {
            StringBuilder sb = new StringBuilder();

            foreach (SFX sfx in GetAllSFXs(false))
            {
                sb.AppendFormat("{0}: equ {1}", sfx.ASMName, sfx.Number);
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendFormat("{0}: equ {1}", "SFX_NONE", SFX.NONE);

            File.WriteAllText(Path.Combine(path, "sfxs-standard.asm"), sb.ToString());
            Console.WriteLine("> Wrote 'sfxs-standard.asm' file!");
        }

        private void WriteASMSoundTest(string path)
        {
            if (_soundTestTemplate == null || path == null) return;

            string template = File.ReadAllText(_soundTestTemplate);
            StringBuilder sb = new StringBuilder(template);
            StringBuilder indexes = new StringBuilder();
            StringBuilder names = new StringBuilder();
            const string padding = "                ";

            Song[] allSongs = GetAllSongs(true);
            SFX[] allSFXs = GetAllSFXs(false);
            int maxNumber = 0;
            int validCount = 0;

            // Get the highest music number
            foreach (Song song in allSongs) { if (song.Name != null) maxNumber = Math.Max(maxNumber, song.Number); }

            // Get the highest SFX number (should always be above the highest music number in practice)
            foreach (SFX sfx in allSFXs) maxNumber = Math.Max(maxNumber, sfx.Number);

            // Populate the name table up to the highest number
            for (int number = 0; number <= maxNumber; number++)
            {
                Song song = Array.Find(allSongs, s => s.Number == number);
                string name = song?.Name ?? string.Empty;

                SFX sfx = Array.Find(allSFXs, s => s.Number == number);
                if (sfx != null) name = "SFX: " + sfx.Name;

                // Name entry is added even if the slot is unused
                if (names.Length > 0) names.Append(padding);
                names.Append("defineName ");
                names.Append('"');
                names.Append(name.Replace("\"", ""));
                names.Append('"');
                names.AppendLine();
            }

            // Add musics to index list
            foreach (Song song in allSongs)
            {
                bool isNotCloneOrIsVanilla = song.Sheet != Sheet.Clone || _banks.Any(b => b.Vanilla.Contains(song));
                if (song != null && song.Name != null && song.ASMName != null && isNotCloneOrIsVanilla)
                {
                    if (indexes.Length > 0) indexes.Append(padding);
                    indexes.AppendFormat("dc.b {0}", song.ASMName);
                    indexes.AppendLine();
                    validCount++;
                }
            }

            // Add SFXs to index list
            foreach (SFX sfx in allSFXs)
            {
                if (indexes.Length > 0) indexes.Append(padding);
                indexes.AppendFormat("dc.b {0}", sfx.ASMName);
                indexes.AppendLine();
                validCount++;
            }

            sb.Replace("{{LAST_INDEX}}", (validCount - 1).ToString());
            sb.Replace("{{INDEXES}}", indexes.ToString());
            sb.Replace("{{NAMES}}", names.ToString());

            File.WriteAllText(Path.Combine(path, "soundtest-standard.asm"), sb.ToString());
            Console.WriteLine("> Wrote 'soundtest-standard.asm' file!");
        }

        private void WriteFMInstruments(string path)
        {
            File.WriteAllBytes(Path.Combine(path, "yminst-standard.bin"), Instruments.ToArray());
            Console.WriteLine("> Wrote 'yminst-standard.bin' file!");
        }

        private void WriteSamples(string pcmSamplesPath, string pcmBanksPath)
        {
            Samples.Write(pcmSamplesPath, pcmBanksPath);
        }

        /// <summary>
        /// Get the music number of the other element of a pair of linked musics.
        /// </summary>
        public int GetPairedMusic(int number)
        {
            if (_musicPairs == null)
                return 0;

            int index = Array.IndexOf(_musicPairs, number);
            if (index >= 0)
            {
                if (index % 2 == 0)
                    return _musicPairs[index + 1];
                else
                    return _musicPairs[index - 1];
            }
            return 0;
        }

        /// <summary>
        /// Verify that given replaced SFX numbers, we will end up in a valid state where all dependencies are still satisfied.
        /// Otherwise, throw an exception with a friendly explanation message for the user.
        /// </summary>
        public void VerifyReplaceSFXAllowed(HashSet<int> replacedSfxNumbers)
        {
            if (_sfxNeededByMap == null) return;

            foreach (int replacedNumber in replacedSfxNumbers)
            {
                if (_sfxNeededByMap.TryGetValue(replacedNumber, out HashSet<int> neededBy))
                {
                    if (!replacedSfxNumbers.IsSupersetOf(neededBy))
                    {
                        int[] numbers = neededBy.ToArray();
                        Array.Sort(numbers);

                        string csv = string.Join(", ", numbers);
                        throw new NotSupportedException("You are attempting to replace SFX " + replacedNumber + " but other SFXs depend on this SFX: " + csv
                            + Environment.NewLine + "You must also provide replacements for *all* SFXs in this list before being allowed to proceed."
                            + Environment.NewLine + "NOTE: these dependencies are purely artificial: SFXs only re-use empty channel data from other SFXs.");
                    }
                }
            }
        }

        /// <summary>
        /// Verify the specified patch file supports the provided patch and return it if is enabled.
        /// </summary>
        public static bool VerifyPatch(string patchesPath, string disasmName, string patchName, string featureBranchName)
        {
            Dictionary<string, int> map = Tools.ReadASMEnumMap(patchesPath);
            if (!map.TryGetValue(patchName, out int value))
            {
                throw new NotSupportedException("You are attempting to use this tool in a " + disasmName + " folder that doesn't support '" + patchName + "' patch."
                        + Environment.NewLine + "Please merge '" + featureBranchName + "' branch into your project and try again!");
            }
            return value >= 1;
        }
    }
}
