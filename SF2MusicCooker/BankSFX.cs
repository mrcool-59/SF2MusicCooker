using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SF2MusicCooker
{
    public sealed class BankSFX
    {
        private readonly List<SFX> _custom = new List<SFX>();
        private readonly List<SFX> _vanilla = new List<SFX>();

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
        /// List of custom SFXs in the bank.
        /// </summary>
        public IReadOnlyList<SFX> Custom { get { return _custom; } }

        /// <summary>
        /// List of vanilla SFXs in the bank.
        /// </summary>
        public IReadOnlyList<SFX> Vanilla { get { return _vanilla; } }

        /// <summary>
        /// Add SFX to this bank.
        /// </summary>
        public void Add(SFX sfx, bool vanilla = false)
        {
            if (Find(sfx.Number, out _) != null) throw new InvalidOperationException("SFX number " + sfx.Number + " is already used in " + Name);

            List<SFX> destination = vanilla ? _vanilla : _custom;
            destination.Add(sfx);
        }

        /// <summary>
        /// Remove SFX from this bank and return it.
        /// </summary>
        public SFX Remove(int number, out bool vanilla)
        {
            int index = _vanilla.FindIndex(s => s.Number == number);
            if (index >= 0)
            {
                SFX removed = _vanilla[index];
                _vanilla.RemoveAt(index);
                vanilla = true;
                return removed;
            }

            index = _custom.FindIndex(s => s.Number == number);
            if (index >= 0)
            {
                SFX removed = _custom[index];
                _custom.RemoveAt(index);
                vanilla = false;
                return removed;
            }

            throw new InvalidOperationException("SFX number " + number + " doesn't exist in " + Name);
        }

        /// <summary>
        /// Find SFX by number in this bank.
        /// </summary>
        public SFX Find(int number, out bool vanilla)
        {
            vanilla = false;

            SFX sfx = _vanilla.Find(s => s.Number == number);
            if (sfx != null)
            {
                vanilla = true;
                return sfx;
            }

            return _custom.Find(s => s.Number == number);
        }

        /// <summary>
        /// Generate an ASM file describing this bank.
        /// </summary>
        public void Write(string path)
        {
            StringBuilder output = new StringBuilder(File.ReadAllText("sfxbank.asm.tpl"));

            SFX[] allSFXs = _custom.Concat(_vanilla).ToArray();
            if (allSFXs.Length == 0) throw new InvalidOperationException(Name + " must contain at least 1 SFX.");

            SFX lastSfx = allSFXs.Last();

            const int firstNumber = SFX.FIRST;
            int maxNumber = allSFXs.Max(s => s.Number);

            // Write pointers
            for (int number = firstNumber; number <= maxNumber; number++)
            {
                SFX sfx = Find(number, out bool vanilla) ?? lastSfx;
                output.AppendFormat("    dw {0}\t; #{1} - {2}", sfx.PointerName, sfx.Number, sfx.ASMName);
                if (!vanilla) output.Append(" [Custom]");
                output.AppendLine();
            }

            // Write sheets (inline)
            foreach (SFX sfx in allSFXs)
            {
                output.Append(sfx.Sheet);
            }

            string filename = Name + ".asm";
            File.WriteAllText(Path.Combine(path, filename), output.ToString());
            Console.WriteLine("> Wrote '{0}' file!", filename);
        }

        /// <summary>
        /// Return the estimated size of the bank in bytes.
        /// </summary>
        public int GetEstimatedSize()
        {
            int bytes = 0;
            foreach (SFX sfx in _custom.Concat(_vanilla))
            {
                bytes += AsmSheetToolkit.EstimateBytes(sfx.Sheet);
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

        public BankSFX(string name, string folderName, int maxSize)
        {
            if (maxSize <= 0) throw new ArgumentOutOfRangeException(nameof(maxSize), "cannot be zero or negative");

            Name = name ?? throw new ArgumentNullException(nameof(name));
            FolderName = folderName ?? name;
            MaxSize = maxSize;
        }
    }
}
