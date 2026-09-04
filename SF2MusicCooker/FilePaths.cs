using System;
using System.IO;

namespace SF2MusicCooker
{
    public sealed class FilePaths
    {
        public readonly string MusicNumbersAndAsmNames;
        public readonly string SfxNumbersAndAsmNames;
        public readonly string[] MusicBankFolders;
        public readonly string[] SfxBankFolders;
        public readonly string[] PcmBankFiles;
        public readonly string PcmSamples;
        public readonly string YmInstBin;
        public readonly string YmFrequencies;
        public readonly string PsgFrequencies;
        public readonly string NoteNames;
        public readonly string MusicNamesTxt;
        public readonly string SoundTestFolder;

        /// <summary>
        /// Build file paths appropriate in a generic way.
        /// </summary>
        public FilePaths(string musicNumbersAndAsmNames, string sfxNumbersAndAsmNames,
            string[] musicBankFolders, string[] sfxBankFolders, string[] pcmBankFiles,
            string pcmSamples, string ymInstBin, string ymFrequencies, string psgFrequencies,
            string noteNames = null, string musicNamesTxt = null, string soundTestFolder = null)
        {
            MusicNumbersAndAsmNames = musicNumbersAndAsmNames ?? throw new ArgumentNullException(nameof(musicNumbersAndAsmNames));
            SfxNumbersAndAsmNames = sfxNumbersAndAsmNames ?? throw new ArgumentNullException(nameof(sfxNumbersAndAsmNames));
            MusicBankFolders = musicBankFolders ?? throw new ArgumentNullException(nameof(musicBankFolders));
            SfxBankFolders = sfxBankFolders ?? throw new ArgumentNullException(nameof(sfxBankFolders));
            PcmBankFiles = pcmBankFiles ?? throw new ArgumentNullException(nameof(pcmBankFiles));
            PcmSamples = pcmSamples ?? throw new ArgumentNullException(nameof(pcmSamples));
            YmInstBin = ymInstBin ?? throw new ArgumentNullException(nameof(ymInstBin));
            YmFrequencies = ymFrequencies ?? throw new ArgumentNullException(nameof(ymFrequencies));
            PsgFrequencies = psgFrequencies ?? throw new ArgumentNullException(nameof(psgFrequencies));
            NoteNames = noteNames;
            MusicNamesTxt = musicNamesTxt;
            SoundTestFolder = soundTestFolder;
        }

        /// <summary>
        /// Build file paths appropriate for SF2DISASM (numbers, names, sheets, FM instruments, samples).
        /// </summary>
        public FilePaths(string rootFolder)
        {
            if (rootFolder == null) throw new ArgumentNullException(nameof(rootFolder));

            string soundFolder = Path.Combine(rootFolder, "disasm\\data\\sound");
            string driverFolder = Path.Combine(rootFolder, "disasm\\code\\common\\tech\\sound\\cubewiz\\data");

            MusicNumbersAndAsmNames = Path.Combine(rootFolder, "disasm\\enums\\musics.asm");
            SfxNumbersAndAsmNames = Path.Combine(rootFolder, "disasm\\enums\\sfxs.asm");
            MusicBankFolders = new string[2]
            {
                Path.Combine(soundFolder, "musicbank0"),
                Path.Combine(soundFolder, "musicbank1"),
            };
            SfxBankFolders = new string[1]
            {
                Path.Combine(soundFolder, "sfxbank")
            };
            PcmBankFiles = new string[2]
            {
                Path.Combine(soundFolder, "pcmbank0.bin"),
                Path.Combine(soundFolder, "pcmbank1.bin"),
            };
            PcmSamples = Path.Combine(driverFolder, "pcm_samples.asm");
            YmInstBin = Path.Combine(soundFolder, "yminst.bin");
            YmFrequencies = Path.Combine(driverFolder, "ym_frequencies.asm");
            PsgFrequencies = Path.Combine(driverFolder, "psg_frequencies.asm");
            NoteNames = Path.Combine(soundFolder, "enums.asm");
            MusicNamesTxt = Path.Combine(soundFolder, "musicnames.txt");
            SoundTestFolder = Path.Combine(rootFolder, "disasm\\code\\specialscreens\\witch");
        }
    }
}
