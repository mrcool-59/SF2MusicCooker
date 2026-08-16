using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SF2MusicCooker
{
    public sealed class Bank
    {
        private static readonly string[] bankNames = new string[4]
        {
            "musicbank0",
            "musicbank1",
            "musicbankext0",
            "musicbankext1",
        };

        private static readonly int[] bankOrigins = new int[4]
        {
            1,
            33,
            49,
            57,
        };

        private static readonly int[] bankLengths = new int[4]
        {
            32,
            16,
            8,
            8,
        };

        private readonly List<Song> _custom = new List<Song>();
        private readonly List<Song> _vanilla = new List<Song>();

        /// <summary>
        /// ID of this bank (0, 1 for vanilla banks and 2, 3 for extra banks).
        /// </summary>
        public readonly int ID;

        /// <summary>
        /// Name of this bank.
        /// </summary>
        public string Name { get { return bankNames[ID]; } }

        /// <summary>
        /// Folder name of this bank.
        /// </summary>
        public string FolderName { get { return bankNames[ID] + (ID <= 1 ? "-standard" : ""); } }

        /// <summary>
        /// Base music number of this bank.
        /// </summary>
        public int BaseNumber { get { return bankOrigins[ID]; } }

        /// <summary>
        /// Last music number of this bank.
        /// </summary>
        public int LastNumber { get { return bankOrigins[ID] + bankLengths[ID] - 1; } }

        /// <summary>
        /// List of custom songs in the bank.
        /// </summary>
        public IReadOnlyList<Song> Custom { get { return _custom; } }

        /// <summary>
        /// List of vanilla songs in the bank.
        /// </summary>
        public IReadOnlyList<Song> Vanilla { get { return _vanilla; } }

        /// <summary>
        /// True if a music number belongs to this bank.
        /// </summary>
        public bool InRange(int number)
        {
            return number >= BaseNumber && number <= LastNumber;
        }

        /// <summary>
        /// Add a song to this bank.
        /// </summary>
        public void Add(Song song, bool vanilla = false)
        {
            if (!InRange(song.Number)) throw new InvalidOperationException("Music number " + song.Number + " cannot be put into " + Name);
            if (Find(song.Number, out _) != null) throw new InvalidOperationException("Music number " + song.Number + " is already used in " + Name);

            List<Song> destination = vanilla ? _vanilla : _custom;
            destination.Add(song);
        }

        /// <summary>
        /// Replace a vanilla song by the provided song.
        /// </summary>
        public void Replace(Song song)
        {
            Song existing = Find(song.Number, out bool vanilla);
            if (existing == null || !vanilla) throw new InvalidOperationException("Music number " + song.Number + " is not a vanilla music that can be replaced");

            _vanilla.Remove(existing);
            _custom.Add(new Song(song.Number, song.Name, existing.ASMName, song.Sheet));
        }

        /// <summary>
        /// Remove a vanilla song and return it.
        /// </summary>
        public Song Remove(int number)
        {
            Song existing = Find(number, out bool vanilla);
            if (existing == null || !vanilla) throw new InvalidOperationException("Music number " + number + " is not a vanilla music that can be move-replaced");

            _vanilla.Remove(existing);
            return existing;
        }

        /// <summary>
        /// Add an empty music for the given music number if the music number is not already used.
        /// </summary>
        public void Pad(int number, FMInstruments instruments)
        {
            if (Find(number, out _) == null)
            {
                string sheet = AsmSheetWriter.Write(FurnaceFile.Empty, Options.Default, instruments, number);
                _vanilla.Add(new Song(number, null, null, sheet));
            }
        }

        /// <summary>
        /// Find a song by number in this bank.
        /// </summary>
        public Song Find(int number, out bool vanilla)
        {
            vanilla = false;

            Song song = _vanilla.Find(s => s.Number == number);
            if (song != null)
            {
                vanilla = true;
                return song;
            }

            return _custom.Find(s => s.Number == number);
        }

        /// <summary>
        /// Generate an ASM file describing this bank.
        /// </summary>
        public void Write(string path)
        {
            StringBuilder output = new StringBuilder(File.ReadAllText("musicbank.asm.tpl"));

            int from = BaseNumber;
            int to = LastNumber;
            int segmentStart = ID == 0 ? 0 : 32;

            for (int i = 1; i <= 32; i++)
            {
                int number = segmentStart + i;
                if (number >= from && number <= to)
                {
                    Song song = Find(number, out _);
                    output.Append("\t\tdw Music_");
                    output.Append(song == null ? to : number);
                    output.AppendLine();
                }
                else
                {
                    output.Append("\t\tdw Music_");
                    output.Append(to);
                    output.Append("\t\t; SHADOWED");
                    output.AppendLine();
                }
            }

            for (int number = from; number <= to; number++)
            {
                Song song = Find(number, out bool vanilla);
                if (song == null && number == to)
                {
                    throw new InvalidOperationException("You should call 'PadLast' before calling 'Write'");
                }
                if (song != null && song.Sheet != null)
                {
                    output.Append("\t\tinclude \"");
                    output.Append(GetMusicFilename(number));
                    output.Append("\"");
                    if (!vanilla) output.AppendFormat("\t; [CUSTOM] {0}", song.Name);
                    output.AppendLine();
                }
            }

            string filename = Name + ".asm";
            File.WriteAllText(Path.Combine(path, filename), output.ToString());
            Console.WriteLine("> Wrote '{0}' file!", filename);
        }

        /// <summary>
        /// Generate ASM files for musics contained in this bank.
        /// </summary>
        public void WriteSheets(string path)
        {
            WriteSheets(_vanilla, path);
            WriteSheets(_custom, path);
        }

        private void WriteSheets(List<Song> songs, string path)
        {
            foreach (Song song in songs)
            {
                if (song.Sheet != null) // Can be null in the special case of paired vanilla musics (3, 4) and (13, 14)
                {
                    string filename = GetMusicFilename(song.Number);
                    File.WriteAllText(Path.Combine(path, filename), song.Sheet);
                    Console.WriteLine("> Wrote '" + filename + "' file!");
                }
            }
        }

        /// <summary>
        /// Print estimated size of the bank and return if it should be considered overloaded.
        /// </summary>
        public bool PrintSize()
        {
            int bytes = 0;
            int maxBytes = 0x8000;

            foreach (Song song in _custom.Concat(_vanilla))
            {
                if (song.Sheet != null)
                {
                    bytes += AsmSheetEstimator.EstimateBytes(song.Sheet, out _);
                }
            }

            float ratio = (float)bytes / maxBytes;
            bool overloaded = ratio >= 0.999f;
            string suffix = overloaded ? " !!! OVERLOADED !!!" : string.Empty;
            string percentage = (ratio * 100f).ToString("0.00", CultureInfo.InvariantCulture);

            Console.WriteLine("Music Bank '{0}' is using approximately {1} bytes out of {2} [{3}%]{4}", Name, bytes, maxBytes, percentage, suffix);
            return overloaded;
        }

        public Bank(int id)
        {
            if (id < 0 || id > 3) throw new ArgumentOutOfRangeException(nameof(id), "must be 0~3");

            ID = id;
        }

        /// <summary>
        /// Get the sheet filename that should be used for a given music number.
        /// </summary>
        public static string GetMusicFilename(int number)
        {
            return "music" + number.ToString("00") + ".asm";
        }
    }
}
