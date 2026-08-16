using System;

namespace SF2MusicCooker
{
    public sealed class SFX
    {
        /// <summary>
        /// The number used for the first SFX.
        /// </summary>
        public const int FIRST = 65;

        /// <summary>
        /// The number used for the 'NONE' SFX.
        /// </summary>
        public const int NONE = 127;

        public readonly int Number;
        public readonly string Name;
        public readonly string ASMName;
        public readonly string PointerName;
        public readonly string Sheet;

        public SFX(int number, string name, string asmName, string pointerName, string sheet)
        {
            if (number < FIRST || number >= NONE)
                throw new NotSupportedException("SFX number must be between " + FIRST + " and " + (NONE - 1));

            if (asmName == null)
                throw new ArgumentNullException(nameof(asmName));

            if (pointerName == null)
                throw new ArgumentNullException(nameof(pointerName));

            if (sheet == null)
                throw new ArgumentNullException(nameof(sheet));

            Number = number;
            Name = name ?? asmName.Replace("SFX_", "");
            ASMName = asmName;
            PointerName = pointerName;
            Sheet = sheet;
        }
    }
}