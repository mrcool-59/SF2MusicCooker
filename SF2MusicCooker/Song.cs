using System;

namespace SF2MusicCooker
{
    public sealed class Song
    {
        public readonly int Number;
        public readonly string Name;
        public readonly string ASMName;
        public readonly Sheet Sheet;

        private readonly int _musicNumberPointerOverride;

        public int PointerNumber { get { return _musicNumberPointerOverride > 0 ? _musicNumberPointerOverride : Number; } }

        public Song(int number, string name, string asmName, Sheet sheet)
        {
            if (number < 1 || number > 64)
                throw new NotSupportedException("Music number must be between 1 and 64");

            if (sheet == null)
                throw new ArgumentNullException(nameof(sheet));

            Number = number;
            Name = name;
            ASMName = asmName;
            Sheet = sheet;
        }

        private Song(int number, string name, string asmName, Sheet sheet, int musicNumberPointerOverride) : this(number, name, asmName, sheet)
        {
            _musicNumberPointerOverride = musicNumberPointerOverride;
        }

        public Song UpdateName(string newName)
        {
            return new Song(Number, newName, ASMName, Sheet);
        }

        public Song UpdateASMName(string newAsmName)
        {
            return new Song(Number, Name, newAsmName, Sheet);
        }

        public Song UpdateSheet(Sheet sheet)
        {
            return new Song(Number, Name, ASMName, sheet);
        }

        public static void CreateSwap(Song vanillaSong, Song customSong, out Song movedVanillaSong, out Song swappedCustomSong)
        {
            movedVanillaSong = new Song(customSong.Number, vanillaSong.Name, vanillaSong.ASMName, vanillaSong.Sheet, vanillaSong.Number);
            swappedCustomSong = new Song(vanillaSong.Number, customSong.Name, customSong.ASMName, customSong.Sheet, customSong.Number);
        }
    }
}
