using System;
using System.IO;

namespace SF2MusicCooker
{
    public sealed class Input
    {
        public readonly FileInfo Fur;
        public readonly int Number;
        public readonly int PairNumber;
        public readonly string PointerName;
        public readonly Options Options;
        public readonly string Name;

        /// <summary>
        /// True if this input is a SFX.
        /// </summary>
        public bool SFX { get { return PointerName != null; } }

        public Input(FileInfo fur, int number, string pointerName, Options options, string name) : this(fur, number, options, name)
        {
            PairNumber = 0;
            PointerName = pointerName ?? throw new ArgumentNullException(nameof(pointerName));
        }

        public Input(FileInfo fur, int number, int pairNumber, Options options, string name) : this(fur, number, options, name)
        {
            PairNumber = pairNumber;
            PointerName = null;
        }

        private Input(FileInfo fur, int number, Options options, string name)
        {
            Fur = fur ?? throw new ArgumentNullException(nameof(fur));
            Number = number;
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }
}