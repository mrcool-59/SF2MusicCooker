using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SF2MusicCooker
{
    public sealed class Output
    {
        private readonly Bank[] _banks;

        public FMInstruments Instruments { get; }

        private Dictionary<int, string> _sfx2name = new Dictionary<int, string>();

        public Output(bool hasExtBanks)
        {
            if (hasExtBanks)
            {
                _banks = new Bank[4]
                {
                    new Bank(0),
                    new Bank(1),
                    new Bank(2),
                    new Bank(3)
                };
            }
            else
            {
                _banks = new Bank[2]
                {
                    new Bank(0),
                    new Bank(1)
                };
            }

            Instruments = new FMInstruments();
        }

        private Bank Find(int number)
        {
            foreach (Bank bank in _banks)
            {
                if (bank.InRange(number))
                    return bank;
            }

            throw new NotSupportedException("Unable to find the appropriate music bank for music number " + number + Environment.NewLine
                 + "You MUST enable extra music banks if you want to use music numbers from 49 to 64.");
        }

        private Song[] GetAllSongs(bool putCustomFirst)
        {
            List<Song> allSongs = new List<Song>();
            if (putCustomFirst) foreach (Bank bank in _banks) allSongs.AddRange(bank.Custom);
            foreach (Bank bank in _banks) allSongs.AddRange(bank.Vanilla);
            if (!putCustomFirst) foreach (Bank bank in _banks) allSongs.AddRange(bank.Custom);
            return allSongs.ToArray();
        }

        /// <summary>
        /// Load vanilla music data from the appropriate SF2DISASM folders (numbers, names, sheets, FM instruments).
        /// </summary>
        public void LoadVanilla(string rootFolder)
        {
            string soundFolder = Path.Combine(rootFolder, "disasm\\data\\sound");
            Dictionary<int, string> number2name = Tools.ReadNumberStringMap(Path.Combine(soundFolder, "musicnames.txt")); // Load names
            Dictionary<int, string> number2enum = Tools.ReadASMEnumReverseMap(Path.Combine(rootFolder, "disasm\\enums\\musics.asm")); // Load numbers
            HashSet<int> usedInstruments = new HashSet<int>();
            Regex regex = new Regex("^music([0-9]+)\\.asm$");

            string[] folders = new string[2]
            {
                Path.Combine(soundFolder, "musicbank0"),
                Path.Combine(soundFolder, "musicbank1"),
            };

            // Load music sheets
            foreach (string folder in folders)
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

                        if (number == 64)
                        {
                            number = 48; // Because we shrinked the second bank, we need to move the padding music to 48 instead of 64
                            sheet = sheet.Replace("Music_64", "Music_48"); // This is hackish but whatever!
                        }

                        Song song = new Song(number, name, asmName, sheet);
                        Bank bank = Find(song.Number);
                        bank.Add(song, true);
                        AsmSheetEstimator.FillInstruments(sheet, usedInstruments);

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

            // Also examine FM instruments used by SFXs, otherwise we will have a problem with muted SFXs because we cleared their instruments...
            string sfxFilename = Path.Combine(soundFolder, "sfxbank", "sfxbank.asm");
            string sfxSheet = File.ReadAllText(sfxFilename);
            AsmSheetEstimator.FillInstruments(sfxSheet, usedInstruments);

            // Load FM instruments and clear the unused ones
            byte[] yminst = File.ReadAllBytes(Path.Combine(soundFolder, "yminst.bin"));
            Instruments.Load(yminst);
            Instruments.ClearExcept(usedInstruments);

            // Load SFX names (to show them on sound test message box)
            _sfx2name = Tools.ReadASMEnumReverseMap(Path.Combine(rootFolder, "disasm\\enums\\sfxs.asm"));
            _sfx2name.Remove(127); // Remove SFX_NONE
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
        /// Replace a vanilla song.
        /// </summary>
        public void Replace(Song song)
        {
            Bank bank = Find(song.Number);
            bank.Replace(song);
        }

        /// <summary>
        /// Remove a vanilla song and return it.
        /// </summary>
        public Song Remove(int number)
        {
            Bank bank = Find(number);
            return bank.Remove(number);
        }

        /// <summary>
        /// Ensure the last music of all banks contains an empty sheet if we didn't provide anything.
        /// </summary>
        public void PadLast()
        {
            foreach (Bank bank in _banks)
            {
                bank.Pad(bank.LastNumber, Instruments);
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
            WriteASMMusicEnum(path);
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
            WriteASMMusicEnum(Path.Combine(rootFolder, "disasm\\enums"));
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
                if (song.ASMName != null)
                {
                    sb.AppendFormat("{0}: equ {1}", song.ASMName, song.Number);
                    sb.AppendLine();
                }
            }

            File.WriteAllText(Path.Combine(path, "musics-standard.asm"), sb.ToString());
            Console.WriteLine("> Wrote 'musics-standard.asm' file!");
        }

        private void WriteASMSoundTest(string path)
        {
            const string filename = "soundtest-standard.asm.tpl";
            if (File.Exists(filename))
            {
                string template = File.ReadAllText(filename);
                StringBuilder sb = new StringBuilder(template);
                StringBuilder indexes = new StringBuilder();
                StringBuilder names = new StringBuilder();
                const string padding = "                ";

                Song[] allSongs = GetAllSongs(true);
                int maxNumber = 0;
                int validCount = 0;

                // Get the highest music number;
                foreach (Song song in allSongs) { if (song.Name != null) maxNumber = Math.Max(maxNumber, song.Number); }

                // Also prepare labels for SFXs
                foreach (var pair in _sfx2name) maxNumber = Math.Max(maxNumber, pair.Key);

                for (int number = 0; number <= maxNumber; number++)
                {
                    Song song = Array.Find(allSongs, s => s.Number == number);
                    string name = song?.Name ?? string.Empty;
                    if (_sfx2name.TryGetValue(number, out string asmName)) name = "SFX: " + asmName.Replace("SFX_", "");

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
                foreach (var pair in _sfx2name)
                {
                    if (indexes.Length > 0) indexes.Append(padding);
                    indexes.AppendFormat("dc.b {0}", pair.Value);
                    indexes.AppendLine();
                    validCount++;
                }

                sb.Replace("{{LAST_INDEX}}", (validCount - 1).ToString());
                sb.Replace("{{INDEXES}}", indexes.ToString());
                sb.Replace("{{NAMES}}", names.ToString());

                File.WriteAllText(Path.Combine(path, "soundtest-standard.asm"), sb.ToString());
                Console.WriteLine("> Wrote 'soundtest-standard.asm' file!");
            }
        }

        private void WriteFMInstruments(string path)
        {
            File.WriteAllBytes(Path.Combine(path, "yminst-standard.bin"), Instruments.ToArray());
            Console.WriteLine("> Wrote 'yminst-standard.bin' file!");
        }

        /// <summary>
        /// Get the music number of the other element of a pair of linked musics in the SF2DISASM.
        /// If those musics are reorganized properly, it will be possible to remove this hack in the future.
        /// </summary>
        public static int GetPairedMusic(int number)
        {
            if (number == 3)
                return 4;
            else if (number == 4)
                return 3;
            else if (number == 13)
                return 14;
            else if (number == 14)
                return 13;

            return 0;
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
