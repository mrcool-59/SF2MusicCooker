using SF2MusicCooker.Furnace;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SF2MusicCooker
{
    public sealed class PCMInstruments
    {
        private const int MAX_SAMPLE_LENGTH = 0x3000; // This is not really a hard limit, more like a "insanity" limit

        private const int MAX_ENTRIES = 0x7F; // Play sample commands have 7 bits

        public readonly struct PCMSample
        {
            /// <summary>
            /// Frame period of this sample within the sound engine. Used to play the sample at different rates (1 is normal rate).
            /// </summary>
            public readonly int Period; 

            /// <summary>
            /// The bank where the PCM sample data can be accessed.
            /// </summary>
            public readonly int Bank;

            /// <summary>
            /// Length of the sample.
            /// </summary>
            public readonly int Length;

            /// <summary>
            /// Offset from the first byte of the bank.
            /// </summary>
            public readonly int Offset;

            /// <summary>
            /// Get ASM line to declare this PCM sample.
            /// </summary>
            public string ToAsmLine(int baseOffset)
            {
                string Hex(ushort x) => "0" + x.ToString("X") + "h";
                ushort offset = (ushort)(Offset + baseOffset);
                return string.Format("dw {0}, DAC_BANK_{1}, {2}, {3}", ((ushort)Period).ToString().PadLeft(2), (ushort)(Bank + 1), Hex((ushort)Length).PadLeft(6), Hex(offset));
            }

            /// <summary>
            /// Get the ASM label of a bank.
            /// </summary>
            public static int ParseBankString(string x, int max)
            {
                for (int i = 1; i <= max; i++)
                {
                    if (x.Equals("DAC_BANK_" + i, StringComparison.OrdinalIgnoreCase))
                        return i - 1;
                }
                throw new FormatException("Invalid bank string: " + x);
            }

            /// <summary>
            /// Get sample rate from 'period' (borrowed from Wiz code).
            /// </summary>
            public static int ComputeRate(int period)
            {
                return (int)(3012000 * Math.Pow(period + 32.07, -1.573) + 1005);
            }

            /// <summary>
            /// Given a sample 'rate', compute the period to play the sample at.
            /// </summary>
            public static int ComputePeriod(int rate)
            {
                int[] periods = new int[255];
                for (int i = 0; i < periods.Length; i++) periods[i] = i + 1;
                return Tools.SelectMin(periods, period => Math.Abs(ComputeRate(period) - rate));
            }

            public PCMSample(int period, int bank, int length, int offset)
            {
                Period = period;
                Bank = bank;
                Length = length;
                Offset = offset;
            }
        }

        private readonly List<PCMSample> _catalog;
        private readonly byte[][] _banks;
        private readonly string[] _names;
        private readonly int[] _cursors;
        private readonly int _baseOffset;

        private bool FindOrAllocate(byte[] data, out int bank, out int offset, out bool allocated)
        {
            // First try to find
            for (int i = 0; i < _banks.Length; i++)
            {
                offset = Tools.IndexOf(_banks[i], data);
                if (offset >= 0)
                {
                    bank = i;
                    allocated = false;
                    return true;
                }
            }

            // Then try to allocate
            for (int i = 0; i < _banks.Length; i++)
            {
                if (GetRemainingBytes(i) >= data.Length)
                {
                    bank = i;
                    offset = _cursors[i];
                    allocated = true;
                    _cursors[i] += data.Length;
                    Buffer.BlockCopy(data, 0, _banks[i], offset, data.Length);
                    return true;
                }
            }

            bank = -1;
            offset = -1;
            allocated = false;
            return false;
        }

        private void Shift(int bank, int offset, int length)
        {
            byte[] data = _banks[bank];

            // Update cursor
            _cursors[bank] -= length;

            // Shift data region
            int count = _cursors[bank] - offset;
            for (int i = 0; i < count; i++) data[offset + i] = data[offset + i + length];

            // Fill end with zeroes
            Tools.Fill(data, _cursors[bank], length, 0);

            // Update catalog
            for (int i = 0; i < _catalog.Count; i++)
            {
                PCMSample sample = _catalog[i];
                if (sample.Bank != bank) continue;

                Debug.Assert(sample.Offset + sample.Length <= offset || sample.Offset >= offset + length);
                if (sample.Offset >= offset + length) _catalog[i] = new PCMSample(sample.Period, bank, sample.Length, sample.Offset - length);
            }
        }

        private static byte[] Resample(byte[] data, int fromRate, int toRate)
        {
            return data; // TODO: not implemented (NOTE: probably don't resample if close enough...)
        }

        /// <summary>
        /// Number of banks to store PCM samples.
        /// </summary>
        public int NumBanks { get { return _banks.Length; } }

        /// <summary>
        /// Get remaining bytes available to store PCM samples in the specified bank.
        /// </summary>
        public int GetRemainingBytes(int bank)
        {
            return _banks[bank].Length - _cursors[bank];
        }

        /// <summary>
        /// Get the name of a bank.
        /// </summary>
        public string GetBankName(int bank)
        {
            return _names[bank];
        }

        /// <summary>
        /// Print how many bytes is free in each of the PCM banks.
        /// </summary>
        public void PrintSize()
        {
            for (int bank = 0; bank < _cursors.Length; bank++)
            {
                int size = _banks[bank].Length;
                int bytes = _cursors[bank];
                float ratio = (float)bytes / size;
                string percentage = (ratio * 100f).ToString("0.00", CultureInfo.InvariantCulture);

                Console.WriteLine("> Bank '{0}' is using {1} bytes out of {2} [{3}%]", GetBankName(bank), bytes, size, percentage);
            }
        }

        /// <summary>
        /// Add a new sample. Return false if the sample already exists.
        /// </summary>
        public bool Add(byte[] data, int framePeriod)
        {
            if (data.Length > MAX_SAMPLE_LENGTH) throw new NotSupportedException("PCM sample must be under " + MAX_SAMPLE_LENGTH + " bytes");

            if (_catalog.Count >= MAX_ENTRIES) throw new InvalidOperationException("Sorry, it's not possible to have more than " + MAX_ENTRIES + " sample entries");

            if (FindOrAllocate(data, out int bank, out int offset, out bool allocated))
            {
                _catalog.Add(new PCMSample(framePeriod, bank, data.Length, offset));
                return allocated;
            }
            else
            {
                throw new InvalidOperationException("Sorry, there is not enough free space to store PCM sample data in any of the PCM banks!");
            }
        }

        /// <summary>
        /// Add a new Furnace sample. Return false if the sample already exists.
        /// </summary>
        public bool Add(Sample sample, int shift, bool print)
        {
            // TODO: A4 tuning coeff to take in account

            int actualPeriod = PCMSample.ComputePeriod(sample.Rate);
            int actualRate = PCMSample.ComputeRate(actualPeriod);
            byte[] data = Resample(sample.Data, sample.Rate, actualRate);

            int playRate = PitchTable.ShiftFrequency(actualRate, shift);
            int playPeriod = PCMSample.ComputePeriod(playRate);

            bool added = Add(data, playPeriod);
            if (print)
            {
                if (added)
                    Console.WriteLine("+ Added sample '{0}' to PCM bank! [{1} bytes]", sample.Name, data.Length);
                else
                    Console.WriteLine("! A duplicate of sample '{0}' already exists in the PCM bank! [{1} bytes]", sample.Name, data.Length);
            }
            return added;
        }

        /// <summary>
        /// Add samples from a Furnace file.
        /// </summary>
        public void AddMany(FurnaceFile file, Instrument[] usedInstruments, bool print)
        {
            foreach (Instrument instrument in file.Instruments)
            {
                if (instrument.Type == Instrument.DAC && Array.IndexOf(usedInstruments, instrument) >= 0)
                {
                    SampleMap map = FeatureInterpreter.ParseFurnaceSampleInstrument(instrument.Data);

                    for (int note = NoteBible.BASE_VALUE; note <= NoteBible.LAST_VALUE; note++)
                    {
                        SampleMap.Entry entry = map.Read(note);

                        if (!entry.Invalid)
                        {
                            Sample sample = file.Samples[entry.Sample];
                            int shift = entry.Note - NoteBible.C4_VALUE;

                            _ = Add(sample, shift, print);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Remove a sample and free its data region in the PCM bank. This will update offsets of all samples and each bank cursor.
        /// </summary>
        public void Remove(int index)
        {
            // TODO: this will break vanilla songs that rely on this sample index, set dummy data instead!

            _catalog.RemoveAt(index);
        }

        /// <summary>
        /// Fill gaps and move sample/free cursors accordingly. Should be called after removing some samples.
        /// </summary>
        public void FillGaps(int bank)
        {
            byte[] data = _banks[bank];
            int i = 0;

            bool InUse(int position)
            {
                return _catalog.Exists(c => c.Bank == bank && position >= c.Offset && position < c.Offset + c.Length);
            }

            while (i < _cursors[bank])
            {
                if (InUse(i))
                {
                    i++;
                }
                else
                {
                    int j = i + 1;
                    while (j < _cursors[bank] && !InUse(j)) j++;
                    Shift(bank, i, j - i);
                }
            }
        }

        /// <summary>
        /// Fill gaps and move sample/free cursors accordingly. Should be called after removing some samples.
        /// </summary>
        public void FillGaps()
        {
            for (int bank = 0; bank < _banks.Length; bank++) FillGaps(bank);
        }

        /// <summary>
        /// Clear all samples.
        /// </summary>
        public void Clear()
        {
            _catalog.Clear();
            for (int i = 0; i < _banks.Length; i++)
            {
                Tools.Fill(_banks[i], 0, _banks[i].Length, 0);
                _cursors[i] = 0;
            }
        }

        /// <summary>
        /// Clear all samples, except those in the set. Return the number of samples removed.
        /// </summary>
        public int ClearExcept(HashSet<int> set)
        {
            int removed = 0;
            int count = _catalog.Count;
            for (int i = 0; i < count; i++)
            {
                int j = count - i - 1;
                if (!set.Contains(j))
                {
                    Remove(j);
                    removed++;
                }
            }
            if (removed > 0) FillGaps();
            return removed;
        }

        /// <summary>
        /// Load the contents of 'pcm_samples.asm' and PCM banks.
        /// </summary>
        public void Load(string pcmSamplesAsm, byte[][] banks)
        {
            if (pcmSamplesAsm == null) throw new ArgumentNullException(nameof(pcmSamplesAsm));
            if (banks.Length != _banks.Length) throw new ArgumentException("must have length " + _banks.Length, nameof(banks));

            Clear();

            for (int bank = 0; bank < banks.Length; bank++)
            {
                if (banks[bank].Length != _banks[bank].Length)
                    throw new FormatException("Bank '" + GetBankName(bank) + "' must have " + _banks[bank].Length + " bytes");

                Buffer.BlockCopy(banks[bank], 0, _banks[bank], 0, banks[bank].Length);
            }

            string[] lines = Tools.GetAllStringElements(pcmSamplesAsm, new Regex("dw(.+)"));

            foreach (string line in lines)
            {
                string[] values = line.Split(',');

                if (values.Length == 4)
                {
                    int framePeriod = Tools.ConvertASMValue(values[0].Trim());
                    int bank = PCMSample.ParseBankString(values[1].Trim(), banks.Length);
                    int length = Tools.ConvertASMValue(values[2].Trim());
                    int offset = Tools.ConvertASMValue(values[3].Trim()) - _baseOffset;

                    _catalog.Add(new PCMSample(framePeriod, bank, length, offset));
                    _cursors[bank] = Math.Max(_cursors[bank], length + offset);
                }
                else
                {
                    throw new FormatException("Malformed 'pcm_samples.asm' line: " + line);
                }
            }
        }

        /// <summary>
        /// Load the 'pcm_samples.asm' file from specified path and PCM banks from the specified folder.
        /// </summary>
        public void Load(string pcmSamplesPath, string[] pcmBanksFiles)
        {
            if (pcmSamplesPath == null) throw new ArgumentNullException(nameof(pcmSamplesPath));
            if (pcmBanksFiles == null) throw new ArgumentNullException(nameof(pcmBanksFiles));

            if (pcmBanksFiles.Length != _banks.Length) throw new NotSupportedException("At least 1 PCM bank is missing!");

            byte[][] banks = new byte[pcmBanksFiles.Length][];
            for (int i = 0; i < pcmBanksFiles.Length; i++) banks[i] = File.ReadAllBytes(pcmBanksFiles[i]);

            string asm = File.ReadAllText(pcmSamplesPath);
            Load(asm, banks);
        }

        /// <summary>
        /// Write the 'pcm_samples.asm' file to specified path and PCM banks to the specified folder.
        /// </summary>
        public void Write(string pcmSamplesPath, string pcmBanksFolder)
        {
            if (pcmSamplesPath == null) throw new ArgumentNullException(nameof(pcmSamplesPath));
            if (pcmBanksFolder == null) throw new ArgumentNullException(nameof(pcmBanksFolder));

            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.Append("; Playback period (higher=slower), bank index, length, offset");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("PCM_SAMPLE_ENTRIES:");
            foreach (PCMSample sample in _catalog)
            {
                sb.Append("    ");
                sb.AppendLine(sample.ToAsmLine(_baseOffset));
            }
            File.WriteAllText(Path.Combine(pcmSamplesPath, "pcm_samples-standard.asm"), sb.ToString().TrimEnd());
            Console.WriteLine("> Wrote 'pcm_samples-standard.asm' file!");

            for (int bank = 0; bank < _banks.Length; bank++)
            {
                string filename = GetBankName(bank) + "-standard.bin";
                File.WriteAllBytes(Path.Combine(pcmBanksFolder, filename), _banks[bank]);
                Console.WriteLine("> Wrote '{0}' file!", filename);
            }
        }

        public PCMInstruments(int[] bankLengths, string[] bankNames, int baseOffset)
        {
            if (bankLengths == null) throw new ArgumentNullException(nameof(bankLengths));
            if (bankNames == null) throw new ArgumentNullException(nameof(bankNames));
            if (baseOffset < 0) throw new ArgumentOutOfRangeException(nameof(baseOffset), "must be zero or positive");

            if (bankLengths.Length != bankNames.Length) throw new ArgumentException("'bankLengths' and 'bankNames' must have the same length");

            _catalog = new List<PCMSample>();
            _banks = new byte[bankLengths.Length][];
            _names = bankNames;
            _cursors = new int[bankLengths.Length];
            _baseOffset = baseOffset;

            for (int i = 0; i < bankLengths.Length; i++)
            {
                byte[] buffer = new byte[bankLengths[i]];
                Tools.Fill(buffer, 0, buffer.Length, 0xFF);
                _banks[i] = buffer;
            }
        }
    }
}
