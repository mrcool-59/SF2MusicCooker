using System;

namespace SF2MusicCooker
{
    public sealed class Song
    {
        public readonly int Number;
        public readonly string Name;
        public readonly string ASMName;
        public readonly string Sheet;

        public Song(int number, string name, string asmName, string sheet)
        {
            if (number < 1 || number > 64)
                throw new NotSupportedException("Music number must be between 1 and 64");

            Number = number;
            Name = name;
            ASMName = asmName;
            Sheet = sheet;
        }

        public Song UpdateName(string newName)
        {
            return new Song(Number, newName, ASMName, Sheet);
        }

        public Song UpdateASMName(string newAsmName)
        {
            return new Song(Number, Name, newAsmName, Sheet);
        }
    }
}
