using Newtonsoft.Json;
using System;
using System.IO;

namespace SF2MusicCooker
{
    public sealed class FilePaths
    {
        [JsonProperty("patches", Required = Required.Always)]
        public readonly string Patches;

        [JsonProperty("musicNumbersAndAsmNames", Required = Required.Always)]
        public readonly string MusicNumbersAndAsmNames;

        [JsonProperty("sfxNumbersAndAsmNames", Required = Required.Always)]
        public readonly string SfxNumbersAndAsmNames;

        [JsonProperty("noteNames", Required = Required.Always)]
        public readonly string NoteNames;

        [JsonProperty("musicBankFolders", Required = Required.Always)]
        public readonly string[] MusicBankFolders;

        [JsonProperty("sfxBankFolders", Required = Required.Always)]
        public readonly string[] SfxBankFolders;

        [JsonProperty("pcmBankFiles", Required = Required.Always)]
        public readonly string[] PcmBankFiles;

        [JsonProperty("pcmSamples", Required = Required.Always)]
        public readonly string PcmSamples;

        [JsonProperty("ymInstBin", Required = Required.Always)]
        public readonly string YmInstBin;

        [JsonProperty("ymFrequencies", Required = Required.Always)]
        public readonly string YmFrequencies;

        [JsonProperty("psgFrequencies", Required = Required.Always)]
        public readonly string PsgFrequencies;

        [JsonProperty("psgInstruments", Required = Required.Always)]
        public readonly string PsgInstruments;

        [JsonProperty("musicNamesTxt", Required = Required.Default)]
        public readonly string MusicNamesTxt;

        [JsonProperty("soundTestFolder", Required = Required.Default)]
        public readonly string SoundTestFolder;

        [JsonConstructor]
        public FilePaths(string patches, string musicNumbersAndAsmNames, string sfxNumbersAndAsmNames, string noteNames,
            string[] musicBankFolders, string[] sfxBankFolders, string[] pcmBankFiles,
            string pcmSamples, string ymInstBin, string ymFrequencies, string psgFrequencies, string psgInstruments,
            string musicNamesTxt = null, string soundTestFolder = null)
        {
            ThrowIfNullOrEmptyElements(musicBankFolders, nameof(musicBankFolders));
            ThrowIfNullOrEmptyElements(sfxBankFolders, nameof(sfxBankFolders));
            ThrowIfNullOrEmptyElements(pcmBankFiles, nameof(pcmBankFiles));

            Patches = patches ?? throw new ArgumentNullException(nameof(patches));
            MusicNumbersAndAsmNames = musicNumbersAndAsmNames ?? throw new ArgumentNullException(nameof(musicNumbersAndAsmNames));
            SfxNumbersAndAsmNames = sfxNumbersAndAsmNames ?? throw new ArgumentNullException(nameof(sfxNumbersAndAsmNames));
            NoteNames = noteNames ?? throw new ArgumentNullException(nameof(noteNames));
            MusicBankFolders = musicBankFolders ?? throw new ArgumentNullException(nameof(musicBankFolders));
            SfxBankFolders = sfxBankFolders ?? throw new ArgumentNullException(nameof(sfxBankFolders));
            PcmBankFiles = pcmBankFiles ?? throw new ArgumentNullException(nameof(pcmBankFiles));
            PcmSamples = pcmSamples ?? throw new ArgumentNullException(nameof(pcmSamples));
            YmInstBin = ymInstBin ?? throw new ArgumentNullException(nameof(ymInstBin));
            YmFrequencies = ymFrequencies ?? throw new ArgumentNullException(nameof(ymFrequencies));
            PsgFrequencies = psgFrequencies ?? throw new ArgumentNullException(nameof(psgFrequencies));
            PsgInstruments = psgInstruments ?? throw new ArgumentNullException(nameof(psgInstruments));
            MusicNamesTxt = musicNamesTxt;
            SoundTestFolder = soundTestFolder;
        }

        private static void ThrowIfNullOrEmptyElements(string[] array, string name)
        {
            if (array != null)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    if (string.IsNullOrEmpty(array[i])) throw new FormatException("'" + name + "' elements cannot be null or empty strings");
                }
            }
        }

        /// <summary>
        /// Move file paths.
        /// </summary>
        public FilePaths Move(string rootPath)
        {
            if (rootPath == null) throw new ArgumentNullException(nameof(rootPath));

            string M(string path)
            {
                return string.IsNullOrEmpty(path) ? null : Path.Combine(rootPath, path);
            }

            string[] MA(string[] array)
            {
                if (array == null) return null;
                array = (string[])array.Clone();
                for (int i = 0; i < array.Length; i++) array[i] = M(array[i]);
                return array;
            }

            return new FilePaths(M(Patches), M(MusicNumbersAndAsmNames), M(SfxNumbersAndAsmNames), M(NoteNames),
                MA(MusicBankFolders), MA(SfxBankFolders), MA(PcmBankFiles),
                M(PcmSamples), M(YmInstBin), M(YmFrequencies), M(PsgFrequencies), M(PsgInstruments),
                M(MusicNamesTxt), M(SoundTestFolder));
        }
    }
}