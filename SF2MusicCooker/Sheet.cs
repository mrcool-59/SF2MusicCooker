using System;

namespace SF2MusicCooker
{
    public sealed class Sheet
    {
        private string _asm;
        private Func<string> _builder;

        /// <summary>
        /// Build the ASM. Does nothing if the ASM is already built.
        /// </summary>
        public void Build()
        {
            if (_builder != null)
            {
                _asm = _builder();
                _builder = null;
            }
        }

        /// <summary>
        /// Build if necessary, then get the ASM.
        /// </summary>
        public string BuildAndGet()
        {
            Build();
            return ASM;
        }

        /// <summary>
        /// Get the ASM or throw an exception if it's not built yet.
        /// </summary>
        public string ASM
        {
            get
            {
                if (_builder != null)
                    throw new InvalidOperationException("Build must be called first");
                else
                    return _asm ?? throw new InvalidOperationException("ASM is null");
            }
        }

        private Sheet(string asm, Func<string> builder)
        {
            _asm = asm;
            _builder = builder;
        }

        /// <summary>
        /// Build a sheet that immediately contains ASM payload.
        /// </summary>
        public Sheet(string asm)
        {
            _asm = asm ?? throw new ArgumentNullException(nameof(asm));
            _builder = null;
        }

        /// <summary>
        /// Build a sheet that will be built later upon calling Build method.
        /// </summary>
        public static Sheet Later(Func<string> builder)
        {
            return new Sheet(null, builder);
        }

        /// <summary>
        /// Represents a music sheet that is a clone of another music. This sheet doesn't contain any ASM and will throw if attempting to get it.
        /// </summary>
        public static Sheet Clone = new Sheet(null, null);

        public static implicit operator string(Sheet sheet) => sheet.ASM;
    }
}
