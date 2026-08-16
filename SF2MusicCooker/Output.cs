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
        private readonly Bank[] _banks;
        private readonly BankSFX[] _sfxBanks;
        private readonly int[] _musicPairs;
        private readonly string _soundTestTemplate;

        private Dictionary<int, HashSet<int>> _sfxNeededByMap;

        public FMInstruments Instruments { get; }

        public Output(Bank[] banks, BankSFX[] sfxBanks, int instrumentSlots = FMInstruments.MAX_INSTRUMENTS, int[] musicPairs = null, string soundTestTemplate = null)
        {
            if (musicPairs != null && musicPairs.Length % 2 != 0)
                throw new ArgumentException("must contain an even number of elements (to form pairs)", nameof(musicPairs));

            _banks = banks ?? throw new ArgumentNullException(nameof(banks));
            _sfxBanks = sfxBanks ?? throw new ArgumentNullException(nameof(sfxBanks));
            _musicPairs = musicPairs;
            _soundTestTemplate = soundTestTemplate;

            Instruments = new FMInstruments(instrumentSlots);
        }

        /// <summary>
        /// Create Output tailored for Shining Force 2 with SF2DISASM.
        /// </summary>
        public static Output CreateForSF2DISASM(bool hasExtBanks)
        {
            Bank[] banks;

            if (hasExtBanks)
            {
                banks = new Bank[4]
                {
                    new Bank("musicbank0", "musicbank0-standard", 0x8000, 1, 32),
                    new Bank("musicbank1", "musicbank1-standard", 0x8000, 33, 16), // Shrinked compared to vanilla
                    new Bank("musicbankext0", "musicbankext0-standard", 0x8000, 49, 8), // Extra bank 1
                    new Bank("musicbankext1", "musicbankext1-standard", 0x8000, 57, 8) // Extra bank 1
                };
            }
            else
            {
                banks = new Bank[2]
                {
                    new Bank("musicbank0", "musicbank0-standard", 0x8000, 1, 32),
                    new Bank("musicbank1", "musicbank1-standard", 0x8000, 33, 32)
                };
            }

            BankSFX[] sfxBanks = new BankSFX[1]
            {
                // Quick calculation of available size for SFX bank: 64k - 44k (PCM bank 0-1) - 4k (FM instruments) - 8k (sound driver) = 8k remaining
                new BankSFX("sfxbank", "sfxbank-standard", 0x2000)
            };

            return new Output(banks, sfxBanks, FMInstruments.MAX_INSTRUMENTS, new int[] { 3, 4, 13, 14 }, "soundtest-standard.asm.tpl");
        }

        private Bank Find(int number)
        {
            foreach (Bank bank in _banks)
            {
                if (bank.InRange(number))
                    return bank;
            }

            throw new NotSupportedException("Unable to find the appropriate music bank for music number " + number);
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
        /// Load vanilla music data from the appropriate SF2DISASM folders (numbers, names, sheets, FM instruments).
        /// </summary>
        public void LoadVanilla(string rootFolder)
        {
            string soundFolder = Path.Combine(rootFolder, "disasm\\data\\sound");

            string pathToMusicNumbersAndAsmNames = Path.Combine(rootFolder, "disasm\\enums\\musics.asm");
            string pathToSfxNumbersAndAsmNames = Path.Combine(rootFolder, "disasm\\enums\\sfxs.asm");
            string[] pathToMusicBankFolders = new string[2]
            {
                Path.Combine(soundFolder, "musicbank0"),
                Path.Combine(soundFolder, "musicbank1"),
            };
            string[] pathToSfxBankFiles = new string[1]
            {
                Path.Combine(soundFolder, "sfxbank", "sfxbank.asm")
            };
            string pathToYmInstBin = Path.Combine(soundFolder, "yminst.bin");
            string pathToMusicNamesTxt = Path.Combine(soundFolder, "musicnames.txt");

            LoadVanilla(pathToMusicNumbersAndAsmNames, pathToSfxNumbersAndAsmNames, pathToMusicBankFolders, pathToSfxBankFiles, pathToYmInstBin, pathToMusicNamesTxt);
        }

        /// <summary>
        /// Load vanilla music data from specific folders (outside of SF2, this method is able to read data for other Cube games with similar input files).
        /// </summary>
        public void LoadVanilla(string pathToMusicNumbersAndAsmNames, string pathToSfxNumbersAndAsmNames, string[] pathToMusicBankFolders, string[] pathToSfxBankFiles, string pathToYmInstBin, string pathToMusicNamesTxt = null)
        {
            if (pathToMusicNumbersAndAsmNames == null) throw new ArgumentNullException(nameof(pathToMusicNumbersAndAsmNames));
            if (pathToSfxNumbersAndAsmNames == null) throw new ArgumentNullException(nameof(pathToSfxNumbersAndAsmNames));
            if (pathToMusicBankFolders == null) throw new ArgumentNullException(nameof(pathToMusicBankFolders));
            if (pathToSfxBankFiles == null) throw new ArgumentNullException(nameof(pathToSfxBankFiles));
            if (pathToYmInstBin == null) throw new ArgumentNullException(nameof(pathToYmInstBin));

            Dictionary<int, string> number2name = pathToMusicNamesTxt != null ? Tools.ReadNumberStringMap(pathToMusicNamesTxt) : new Dictionary<int, string>(); // Load music names (for sound test)
            Dictionary<int, string> number2enum = Tools.ReadASMEnumReverseMap(pathToMusicNumbersAndAsmNames); // Load numbers and ASM names
            Dictionary<int, string> sfx_number2enum = Tools.ReadASMEnumReverseMap(pathToSfxNumbersAndAsmNames); // Load numbers and ASM names for SFXs
            HashSet<int> usedInstruments = new HashSet<int>();
            Regex regex = new Regex("^music([0-9]+)\\.asm$");
            Regex sfxPointerNameRegex = new Regex("dw[ \t]+(Sfx_[0-9]+)[ \t\r\n]");
            Regex sfxPointerNameLabelRegex = new Regex("(Sfx_[0-9]+):");

            // Load music sheets from music banks
            foreach (string folder in pathToMusicBankFolders)
            {
                foreach (string filename in Directory.GetFiles(folder, "music*.asm", SearchOption.TopDirectoryOnly))
                {
                    Match match = regex.Match(Path.GetFileName(filename));
                    if (match.Success)
                    {
                        int number = int.Parse(match.Groups[1].Value);
                        number2name.TryGetValue(number, out string name);
                        number2enum.TryGetValue(number, out string asmName);
                        string sheet = File.ReadAllText(filename);

                        // Don't add unreachable musics (i.e: those who don't have a defined ASM name)
                        // These pseudo-musics are probably used for adding sentinel values in case sound driver reads out of bounds data
                        // We are going to add our own sentinel music anyway
                        if (asmName == null) continue;

                        Song song = new Song(number, name, asmName, sheet);
                        Bank bank = Find(song.Number);
                        bank.Add(song, true);
                        AsmSheetToolkit.FillInstruments(sheet, usedInstruments);

                        // Special case for music 4 and 14
                        int pairNumber = GetPairedMusic(number);
                        if (pairNumber > 0)
                        {
                            _ = number2name.TryGetValue(pairNumber, out string pairName);
                            _ = number2enum.TryGetValue(pairNumber, out string pairAsmName);
                            Song pairSong = new Song(pairNumber, pairName, pairAsmName, null);

                            Bank pairBank = Find(pairNumber);
                            pairBank.Add(pairSong, true);
                        }
                    }
                }
            }

            // Load SFX banks (usually only 1)
            int currentBank = 0;
            int currentSfx = SFX.FIRST;
            foreach (string filename in pathToSfxBankFiles)
            {
                string compositeSheet = File.ReadAllText(filename);
                string[] pointerNames = Tools.GetAllStringElements(compositeSheet, sfxPointerNameRegex);
                BankSFX bank = _sfxBanks[currentBank];

                foreach (string pointerName in pointerNames)
                {
                    int number = currentSfx;
                    sfx_number2enum.TryGetValue(number, out string asmName);

                    // Don't add unreachable SFXs (i.e: those who don't have a defined ASM name)
                    if (asmName == null) continue;

                    string sfxSheet = AsmSheetToolkit.SplitByLabel(compositeSheet, sfxPointerNameLabelRegex, pointerName);

                    SFX sfx = new SFX(number, null, asmName, pointerName, sfxSheet);
                    bank.Add(sfx, true);

                    currentSfx++;
                }

                AsmSheetToolkit.FillInstruments(compositeSheet, usedInstruments);
                currentBank++;
            }

            // Verify that all puzzle pieces fit for SFXs (because of their channel pointers that can reference data defined in other SFXs...)
            _sfxNeededByMap = AsmSheetToolkit.VerifyAndGetDependencies(_sfxBanks);

            // Load FM instruments and clear the unused ones
            byte[] yminst = File.ReadAllBytes(pathToYmInstBin);
            Instruments.Load(yminst);
            Instruments.ClearExcept(usedInstruments);
        }

        /// <summary>
        /// Add a custom song.
        /// </summary>
        public void Add(Song song)
        {
            Bank bank = Find(song.Number);
            bank.Add(song);
        }

        /// <summary>
        /// Replace a song.
        /// </summary>
        public void Replace(Song song, bool includeOriginalName = false)
        {
            Bank bank = Find(song.Number);
            Song originalSong = bank.Remove(song.Number, out _);

            song = song.UpdateASMName(originalSong.ASMName);
            if (includeOriginalName) song = song.UpdateName(GetCombinedName(song.Name, originalSong.Name));
            bank.Add(song);
        }

        /// <summary>
        /// Move-replace a song.
        /// </summary>
        public void MoveReplace(Song song, int moveFromNumber, bool includeOriginalName = false)
        {
            Bank originalBank = Find(moveFromNumber);
            Song originalSong = originalBank.Remove(moveFromNumber, out _);

            Bank bank = Find(song.Number);
            song = song.UpdateASMName(originalSong.ASMName);
            if (includeOriginalName) song = song.UpdateName(GetCombinedName(song.Name, originalSong.Name));
            bank.Add(song);
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
                _ = bank.Pad(bank.LastNumber, Instruments);
            }
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
        }

        /// <summary>
        /// Write generated files to the correct locations in SF2DISASM folder.
        /// </summary>
        public void WriteToSF2DISASM(string rootFolder)
        {
            string soundFolder = Path.Combine(rootFolder, "disasm\\data\\sound");
            foreach (Bank bank in _banks)
            {
                string bankPath = Path.Combine(soundFolder, bank.FolderName);
                DeleteAndCreateFolder(bankPath);
                bank.Write(bankPath);
                bank.WriteSheets(bankPath);
            }
            foreach (BankSFX bank in _sfxBanks)
            {
                string bankPath = Path.Combine(soundFolder, bank.FolderName);
                DeleteAndCreateFolder(bankPath);
                bank.Write(bankPath);
            }
            WriteASMMusicEnum(Path.Combine(rootFolder, "disasm\\enums"));
            WriteASMSfxEnum(Path.Combine(rootFolder, "disasm\\enums"));
            WriteASMSoundTest(Path.Combine(rootFolder, "disasm\\code\\specialscreens\\witch"));
            WriteFMInstruments(soundFolder);
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
            if (_soundTestTemplate == null) return;

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
                if (song != null && song.Name != null && song.ASMName != null)
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
        /// Verify the specified SF2DISASM root folder supports the expanded music feature.
        /// </summary>
        public static void VerifySupport(string rootFolder, out bool isFeatureSupported, out bool hasExtraBanks)
        {
            var map = Tools.ReadASMEnumMap(Path.Combine(rootFolder, "disasm\\sf2patches.asm"));
            isFeatureSupported = map.TryGetValue("EXPANDED_MUSIC_BANKS", out int value);
            hasExtraBanks = value >= 1;
        }
    }
}
