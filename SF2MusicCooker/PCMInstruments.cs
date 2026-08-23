using SF2MusicCooker.Furnace;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SF2MusicCooker
{
    public sealed class PCMInstruments
    {
        private const int MAX_SAMPLE_LENGTH = 0x3000; // This is not really a hard limit, more like a "insanity" limit

        public readonly struct PCMSample
        {
            /// <summary>
            /// Used to play the sample at different rates (1 is normal rate).
            /// </summary>
            public readonly int FramePeriod; 

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
                return string.Format("dw {0}, DAC_BANK_{1}, {2}, {3}", ((ushort)FramePeriod).ToString().PadLeft(2), (ushort)(Bank + 1), Hex((ushort)Length), Hex(offset));
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
            /// Get playback rate from 'period' (borrowed from Wiz code).
            /// </summary>
            public static int ComputePlaybackRate(int period)
            {
                return (int)(3012000 * Math.Pow(period - (-32.07), -1.573) + 1005); // TODO: use this
            }

            /// <summary>
            /// Given a sample rate, compute the 'period' to the sample at roughly the supplied rate.
            /// </summary>
            public static int ComputeFramePeriod(int rate)
            {
                // TODO
                return 0;
            }

            public PCMSample(int framePeriod, int bank, int length, int offset)
            {
                FramePeriod = framePeriod;
                Bank = bank;
                Length = length;
                Offset = offset;
            }
        }

        private readonly List<PCMSample> _catalog;
        private readonly byte[][] _banks;
        private readonly int[] _cursors;
        private readonly int _baseOffset;

        private bool Allocate(int bytes, out ushort bank, out ushort offset)
        {
            // TODO
            
            // TODO: étudier la possibilité d'ajouter pcmbankext0 et pcmbankext1

            bank = 0;
            offset = 0;
            return true;
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
            return "pcmbank" + bank;
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
                float ratio = bytes / size;
                string percentage = (ratio * 100f).ToString("0.00", CultureInfo.InvariantCulture);

                Console.WriteLine("> Bank '{0}' is using {1} bytes out of {2} [{3}%]", GetBankName(bank), bytes, size, percentage);
            }
        }

        /// <summary>
        /// Add a new sample. Return false if the sample already exists.
        /// </summary>
        public bool Add(byte[] data, ushort framePeriod)
        {
            if (data.Length > MAX_SAMPLE_LENGTH) throw new NotSupportedException("PCM sample must be under " + MAX_SAMPLE_LENGTH + " bytes");

            // TODO
            // TODO: find duplicate if it exists instead of cloning data!!

            if (Allocate(data.Length, out ushort bank, out ushort offset))
            {
                _catalog.Add(new PCMSample(framePeriod, bank, (ushort)data.Length, offset));
                return true;
            }
            else
            {
                throw new NotSupportedException("Sorry, there is not enough free space to store PCM sample data in any of the PCM banks!");
            }
        }

        /// <summary>
        /// Add a new Furnace sample. Return false if the sample already exists.
        /// </summary>
        public bool Add(Sample sample, bool print)
        {
            // TODO: probably transform the waveform depening on 'sample.Note' and the actual rate the sound driver can play samples at!

            ushort framePeriod = (ushort)PCMSample.ComputeFramePeriod(sample.Rate);
            int actualRate = PCMSample.ComputePlaybackRate(framePeriod);
            byte[] data = sample.Data;

            if (sample.Rate != actualRate)
            {
                // TODO: resample 'data' I think
            }

            bool added = Add(data, framePeriod);
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
            int index = 0;

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

                            _ = Add(sample, print);
                        }
                    }
                }

                index++;
            }
        }

        /// <summary>
        /// Remove a sample and free its data region in the PCM bank. This will update offsets of all samples and each bank cursor.
        /// </summary>
        public void Remove(int index)
        {
            // TODO: compact if no other sample in the catalog is using the removed region
        }

        /// <summary>
        /// Fill gaps and move sample/free cursors accordingly. Should be called after removing some samples.
        /// </summary>
        public void FillGaps()
        {
            // TODO
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
        /// Clear all samples, except those in the set.
        /// </summary>
        public void ClearExcept(HashSet<int> set)
        {
            int count = _catalog.Count;
            for (int i = 0; i < count; i++)
            {
                int j = count - i - 1;
                if (!set.Contains(j)) Remove(j);
            }
            FillGaps();
        }

        /// <summary>
        /// Load the contents of 'pcm_samples.asm' and PCM banks.
        /// </summary>
        public void Load(string pcmSamplesAsm, byte[][] banks)
        {
            if (pcmSamplesAsm == null) throw new ArgumentNullException(nameof(pcmSamplesAsm));
            if (banks.Length != _banks.Length) throw new ArgumentException("must have length " + _banks.Length, nameof(banks));

            Clear();

            for (int i = 0; i < banks.Length; i++)
            {
                if (banks[i].Length != _banks[i].Length)
                    throw new FormatException("Bank '" + GetBankName(i) + "' must have " + _banks[i].Length + " bytes");

                Buffer.BlockCopy(banks[i], 0, _banks[i], 0, banks[i].Length);
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
            sb.AppendLine("PCM_SAMPLE_ENTRIES:");
            foreach (PCMSample sample in _catalog)
            {
                sb.Append("    ");
                sb.AppendLine(sample.ToAsmLine(_baseOffset));
            }
            File.WriteAllText(pcmSamplesPath, sb.ToString());

            for (int i = 0; i < _banks.Length; i++)
            {
                File.WriteAllBytes(Path.Combine(pcmBanksFolder, GetBankName(i) + "-standard.bin"), _banks[i]);
            }
        }

        public PCMInstruments(int[] bankLengths, int baseOffset)
        {
            if (bankLengths == null) throw new ArgumentNullException(nameof(bankLengths));
            if (baseOffset < 0) throw new ArgumentOutOfRangeException(nameof(baseOffset), "must be zero or positive");

            _catalog = new List<PCMSample>();
            _banks = new byte[bankLengths.Length][];
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
