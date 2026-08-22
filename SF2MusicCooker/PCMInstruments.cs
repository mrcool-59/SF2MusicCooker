using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SF2MusicCooker
{
    public sealed class PCMInstruments
    {
        private const int MAX_SAMPLE_LENGTH = 0x3000; // This is not really a hard limit, more like a "insanity" limit

        public sealed class PCMSample
        {
            /// <summary>
            /// Used to play the sample at different rates (1 is normal rate).
            /// </summary>
            public readonly ushort FramePeriod; 

            /// <summary>
            /// The bank where the PCM sample data can be accessed.
            /// </summary>
            public readonly ushort Bank;

            /// <summary>
            /// Length of the sample.
            /// </summary>
            public readonly ushort Length;

            /// <summary>
            /// Offset from the first byte of the bank.
            /// </summary>
            public readonly ushort Offset;

            /// <summary>
            /// Get ASM line to declare this PCM sample.
            /// </summary>
            public string ToAsmLine()
            {
                string Hex(ushort x) => "0" + x.ToString("X") + "h";
                return string.Format("    dw {0}, DAC_BANK_{1}, {2}, {3}", FramePeriod.ToString().PadLeft(2), Bank, Hex(Length), Hex(Offset));
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

            public PCMSample(ushort framePeriod, ushort bank, ushort length, ushort offset)
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

        private bool Allocate(int bytes, out ushort bank, out ushort offset)
        {
            // TODO
            bank = 0;
            offset = 0;
            return false;
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
        /// Add a new sample.
        /// </summary>
        public void Add(byte[] data, ushort framePeriod)
        {
            if (data.Length > MAX_SAMPLE_LENGTH) throw new NotSupportedException("PCM sample must be under " + MAX_SAMPLE_LENGTH + " bytes");

            // TODO
            // TODO: find duplicate if it exists instead of cloning data!!

            if (Allocate(data.Length, out ushort bank, out ushort offset))
            {
                _catalog.Add(new PCMSample(framePeriod, bank, (ushort)data.Length, offset));
            }
            else
            {
                throw new NotSupportedException("Sorry, there is not enough free space to store PCM sample data in any of the PCM banks!");
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
        /// Load the contents of 'pcm_samples.asm' and PCM banks.
        /// </summary>
        public void Load(string pcmSamplesAsm, byte[][] banks)
        {
            if (pcmSamplesAsm == null) throw new ArgumentNullException(nameof(pcmSamplesAsm));
            if (banks.Length != _banks.Length) throw new ArgumentException("must have length " + _banks.Length, nameof(banks));

            // TODO: parse the ASM file -_-

            // TODO: verify 'banks' have correct size then buffer.blockCopy them
        }

        /// <summary>
        /// Load the 'pcm_samples.asm' file from specified path and PCM banks from the specified folder.
        /// </summary>
        public void Load(string pcmSamplesPath, string pcmBanksFolder)
        {
            if (pcmSamplesPath == null) throw new ArgumentNullException(nameof(pcmSamplesPath));
            if (pcmBanksFolder == null) throw new ArgumentNullException(nameof(pcmBanksFolder));

            string[] files = Directory.GetFiles(pcmBanksFolder, "pcmbank*.bin", SearchOption.TopDirectoryOnly);
            if (files.Length != _banks.Length) throw new NotSupportedException("At least 1 PCM bank is missing!");

            byte[][] banks = new byte[files.Length][];
            for (int i = 0; i < files.Length; i++) banks[i] = File.ReadAllBytes(files[i]);

            Load(File.ReadAllText(pcmSamplesPath), banks);
        }

        /// <summary>
        /// Write the 'pcm_samples.asm' file to specified path and PCM banks to the specified folder.
        /// </summary>
        public void Write(string pcmSamplesPath, string pcmBanksFolder)
        {
            if (pcmSamplesPath == null) throw new ArgumentNullException(nameof(pcmSamplesPath));
            if (pcmBanksFolder == null) throw new ArgumentNullException(nameof(pcmBanksFolder));

            // TODO
        }

        public PCMInstruments(int[] bankLengths)
        {
            if (bankLengths == null) throw new ArgumentNullException(nameof(bankLengths));

            _catalog = new List<PCMSample>();
            _banks = new byte[bankLengths.Length][];
            _cursors = new int[bankLengths.Length];

            for (int i = 0; i < bankLengths.Length; i++)
            {
                byte[] buffer = new byte[bankLengths[i]];
                Tools.Fill(buffer, 0, buffer.Length, 0xFF);
                _banks[i] = buffer;
            }
        }
    }
}
