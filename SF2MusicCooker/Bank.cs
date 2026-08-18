using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SF2MusicCooker
{
    public sealed class Bank
    {
        private readonly List<Song> _custom = new List<Song>();
        private readonly List<Song> _vanilla = new List<Song>();

        /// <summary>
        /// Name of this bank.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// Folder name of this bank.
        /// </summary>
        public readonly string FolderName;

        /// <summary>
        /// Max size of the bank in bytes.
        /// </summary>
        public readonly int MaxSize;

        /// <summary>
        /// Length of this bank.
        /// </summary>
        public readonly int Length;

        /// <summary>
        /// First music number of this bank.
        /// </summary>
        public readonly int FirstNumber;

        /// <summary>
        /// Last music number of this bank.
        /// </summary>
        public int LastNumber { get { return FirstNumber + Length - 1; } }

        /// <summary>
        /// Start index of the physical segment.
        /// </summary>
        public int SegmentOffset { get { return FirstNumber == 1 ? 0 : 32; } } // TODO: SF2 specific hack, will hopefully be removed soon once I figure out how to make extra banks work ingame with bank switching

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
            return number >= FirstNumber && number <= LastNumber;
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
        /// Remove a song from this bank and return it.
        /// </summary>
        public Song Remove(int number, out bool vanilla)
        {
            if (!InRange(number)) throw new InvalidOperationException("Music number " + number + " cannot be put into " + Name);

            int index = _vanilla.FindIndex(s => s.Number == number);
            if (index >= 0)
            {
                Song removed = _vanilla[index];
                _vanilla.RemoveAt(index);
                vanilla = true;
                return removed;
            }

            index = _custom.FindIndex(s => s.Number == number);
            if (index >= 0)
            {
                Song removed = _custom[index];
                _custom.RemoveAt(index);
                vanilla = false;
                return removed;
            }

            throw new InvalidOperationException("Music number " + number + " doesn't exist in " + Name);
        }

        /// <summary>
        /// Add an empty music for the given music number if the music number is not already used.
        /// </summary>
        public bool Pad(int number, FMInstruments instruments)
        {
            if (Find(number, out _) == null)
            {
                string sheet = AsmSheetWriter.Write(Furnace.FurnaceFile.Empty, Options.Default, instruments, number);
                _vanilla.Add(new Song(number, null, null, sheet));
                return true;
            }
            return false;
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

            int from = FirstNumber;
            int to = LastNumber;

            for (int i = 1; i <= 32; i++)
            {
                int number = SegmentOffset + i;
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
                if (song.Sheet != null) // Can be null in the special case of paired musics [for vanilla SF2: musics (3, 4) and (13, 14)]
                {
                    string filename = GetMusicFilename(song.Number);
                    File.WriteAllText(Path.Combine(path, filename), song.Sheet);
                    Console.WriteLine("> Wrote '" + filename + "' file!");
                }
            }
        }

        /// <summary>
        /// Return the estimated size of the bank in bytes.
        /// </summary>
        public int GetEstimatedSize()
        {
            int bytes = 0;
            foreach (Song song in _custom.Concat(_vanilla))
            {
                if (song.Sheet != null) // See above
                {
                    bytes += AsmSheetToolkit.EstimateBytes(song.Sheet);
                }
            }
            return bytes;
        }

        /// <summary>
        /// Print estimated size of the bank and return if it should be considered overloaded.
        /// </summary>
        public bool PrintSize()
        {
            int bytes = GetEstimatedSize();
            float ratio = (float)bytes / MaxSize;
            bool overloaded = ratio >= 0.999f;
            string suffix = overloaded ? " !!! OVERLOADED !!!" : string.Empty;
            string percentage = (ratio * 100f).ToString("0.00", CultureInfo.InvariantCulture);

            Console.WriteLine("> Bank '{0}' is using {1} bytes out of {2} [{3}%]{4}", Name, bytes, MaxSize, percentage, suffix);
            return overloaded;
        }

        public Bank(string name, string folderName, int maxSize, int firstNumber, int length)
        {
            if (maxSize <= 0) throw new ArgumentOutOfRangeException(nameof(maxSize), "cannot be zero or negative");
            if (firstNumber <= 0) throw new ArgumentOutOfRangeException(nameof(firstNumber), "cannot be zero or negative");
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length), "cannot be zero or negative");

            Name = name ?? throw new ArgumentNullException(nameof(name));
            FolderName = folderName ?? name;
            MaxSize = maxSize;
            FirstNumber = firstNumber;
            Length = length;
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
