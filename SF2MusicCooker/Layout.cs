using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace SF2MusicCooker
{
    public sealed class Layout
    {
        [JsonProperty("name", Required = Required.Always)]
        private readonly string _name;

        [JsonProperty("featureBranch", Required = Required.Always)]
        private readonly string _featureBranch;

        [JsonProperty("paths", Required = Required.Always)]
        private FilePaths _paths;

        [JsonProperty("musicBanksExtPatch", Required = Required.Always)]
        private readonly string _musicBanksExtPatch;

        [JsonProperty("musicBanksExt", Required = Required.Always)]
        private readonly Bank[] _musicBanksExt;

        [JsonProperty("musicBanks", Required = Required.Always)]
        private readonly Bank[] _musicBanks;

        [JsonProperty("sfxBanks", Required = Required.Always)]
        private readonly BankSFX[] _sfxBanks;

        [JsonProperty("pcmBanksExtPatch", Required = Required.Always)]
        private readonly string _pcmBanksExtPatch;

        [JsonProperty("pcmBanksExt", Required = Required.Always)]
        private readonly PCMInstruments.BankDefinition[] _pcmBanksExt;

        [JsonProperty("pcmBanks", Required = Required.Always)]
        private readonly PCMInstruments.BankDefinition[] _pcmBanks;

        [JsonProperty("pcmSlotsPatch", Required = Required.Always)]
        private readonly string _pcmSlotsPatch;

        [JsonProperty("pcmSlots", Required = Required.Always)]
        private readonly int _pcmSlots;

        [JsonProperty("ymFrequenciesSlotsPatch", Required = Required.Default)]
        private readonly string _ymFrequenciesSlotsPatch;

        [JsonProperty("ymFrequenciesSlots", Required = Required.Default)]
        private readonly int _ymFrequenciesSlots;

        [JsonProperty("ymInstrumentSlots", Required = Required.Default)]
        private readonly int? _ymInstrumentSlots;

        [JsonProperty("musicPairs", Required = Required.Default)]
        private readonly int[] _musicPairs;

        [JsonProperty("soundTestTemplate", Required = Required.Default)]
        private readonly string _soundTestTemplate;

        [JsonConstructor]
        public Layout(string name, string featureBranch, FilePaths paths,
            string musicBanksExtPatch, Bank[] musicBanksExt, Bank[] musicBanks, BankSFX[] sfxBanks,
            string pcmBanksExtPatch, PCMInstruments.BankDefinition[] pcmBanksExt, PCMInstruments.BankDefinition[] pcmBanks, string pcmSlotsPatch, int pcmSlots,
            string ymFrequenciesSlotsPatch = null, int ymFrequenciesSlots = 0, int? ymInstrumentSlots = null, int[] musicPairs = null, string soundTestTemplate = null)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _featureBranch = featureBranch ?? throw new ArgumentNullException(nameof(featureBranch));

            _paths = paths ?? throw new ArgumentNullException(nameof(paths));

            _musicBanksExtPatch = musicBanksExtPatch ?? throw new ArgumentNullException(nameof(musicBanksExtPatch));
            _musicBanksExt = musicBanksExt ?? throw new ArgumentNullException(nameof(musicBanksExt));
            _musicBanks = musicBanks ?? throw new ArgumentNullException(nameof(musicBanks));

            _sfxBanks = sfxBanks ?? throw new ArgumentNullException(nameof(sfxBanks));

            _pcmBanksExtPatch = pcmBanksExtPatch ?? throw new ArgumentNullException(nameof(pcmBanksExtPatch));
            _pcmBanksExt = pcmBanksExt ?? throw new ArgumentNullException(nameof(pcmBanksExt));
            _pcmBanks = pcmBanks ?? throw new ArgumentNullException(nameof(pcmBanks));

            _pcmSlotsPatch = pcmSlotsPatch ?? throw new ArgumentNullException(nameof(pcmSlotsPatch));
            _pcmSlots = pcmSlots;

            _ymFrequenciesSlotsPatch = ymFrequenciesSlotsPatch;
            _ymFrequenciesSlots = ymFrequenciesSlots;

            _ymInstrumentSlots = ymInstrumentSlots;

            _musicPairs = musicPairs;

            _soundTestTemplate = soundTestTemplate;
        }

        /// <summary>
        /// Verify the patch file supports the provided patch and return it if is enabled.
        /// </summary>
        public bool VerifyPatch(string patchName, out int value)
        {
            if (patchName == null) throw new ArgumentNullException(nameof(patchName));
            Dictionary<string, int> map = Tools.ReadASMEnumMap(_paths.Patches);
            if (!map.TryGetValue(patchName, out value))
            {
                throw new NotSupportedException("You are attempting to use this tool in a " + _name + " repository that doesn't support '" + patchName + "' patch."
                        + Environment.NewLine + "Please merge '" + _featureBranch + "' branch into your project and try again!");
            }
            return value >= 1;
        }

        /// <summary>
        /// Verify the patch file supports the provided optional patch and return it if is enabled.
        /// </summary>
        public bool VerifyOptionalPatch(string patchName, out int value)
        {
            if (patchName == null) { value = 0; return false; }
            Dictionary<string, int> map = Tools.ReadASMEnumMap(_paths.Patches);
            if (!map.TryGetValue(patchName, out value)) return false;
            return value >= 1;
        }

        /// <summary>
        /// Build a new output based on this layout.
        /// </summary>
        public Output Build()
        {
            Console.WriteLine("----- ACTIVE LAYOUT: {0} -----", _name);

            Console.WriteLine("Checking support for 'expanded musics' feature...");
            bool hasExtMusicBanks = VerifyPatch(_musicBanksExtPatch, out _);
            Console.WriteLine("> Feature is supported! This tool may proceed.");
            Console.WriteLine("> Expanded music banks are {0}", hasExtMusicBanks ? "ENABLED" : "DISABLED");

            bool hasExtPcmBanks = VerifyOptionalPatch(_pcmBanksExtPatch, out _);
            Console.WriteLine("> Expanded PCM banks are {0}", hasExtPcmBanks ? "ENABLED" : "DISABLED");

            bool hasExtPcmEntries = VerifyOptionalPatch(_pcmSlotsPatch, out int extPcmEntries);
            Console.WriteLine("> Expanded PCM entries are {0} (value: {1})", hasExtPcmEntries ? "ENABLED" : "DISABLED", extPcmEntries);

            bool hasExtYmFrequencies = VerifyOptionalPatch(_ymFrequenciesSlotsPatch, out int extYmFrequencies);
            Console.WriteLine("> Expanded YM frequencies are {0} (value: {1})", hasExtYmFrequencies ? "ENABLED" : "DISABLED", extYmFrequencies);

            Bank[] musicBanks = hasExtMusicBanks ? Tools.Combine(_musicBanks, _musicBanksExt) : _musicBanks;
            PCMInstruments.BankDefinition[] pcmBanks = hasExtPcmBanks ? Tools.Combine(_pcmBanksExt, _pcmBanks) : _pcmBanks;
            int pcmSlots = hasExtPcmBanks ? extPcmEntries : _pcmSlots;
            int ymFrequenciesSlots = hasExtYmFrequencies ? extYmFrequencies : _ymFrequenciesSlots;
            int ymInstrumentSlots = _ymInstrumentSlots ?? FMInstruments.MAX_SLOTS;

            return new Output(_name, _paths, musicBanks, _sfxBanks, pcmBanks, pcmSlots, ymInstrumentSlots, ymFrequenciesSlots, _musicPairs, _soundTestTemplate);
        }

        /// <summary>
        /// Find the applicable layout.
        /// </summary>
        public static Layout Select(Layout[] layouts)
        {
            foreach (Layout layout in layouts)
            {
                if (File.Exists(layout._paths.Patches)) return layout;
            }
            return null;
        }

        /// <summary>
        /// Load all known layouts.
        /// </summary>
        public static Layout[] Load(string rootFolder, string path)
        {
            string json = File.ReadAllText(path);
            Layout[] layouts = JsonConvert.DeserializeObject<Layout[]>(json);
            for (int i = 0; i < layouts.Length; i++) layouts[i].Move(rootFolder);
            return layouts;
        }

        private void Move(string rootFolder)
        {
            _paths = _paths.Move(rootFolder);
        }
    }
}