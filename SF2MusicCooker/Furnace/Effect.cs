namespace SF2MusicCooker.Furnace
{
    public readonly struct Effect
    {
        public const byte GoTo = 0x0B;
        public const byte GoNext = 0x0D;
        public const byte End = 0xFF;
        public const byte Pan = 0x08;
        public const byte PanTrinary = 0x80;
        public const byte Legato = 0xEA;

        /// <summary>
        /// List of supported effects.
        /// </summary>
        public static readonly byte[] SupportedEffects = new byte[]
        {
            GoTo, GoNext, End, Pan, PanTrinary, Legato
        };

        public readonly byte Type;
        public readonly byte Value;

        public Effect(byte type, byte value)
        {
            Type = type;
            Value = value;
        }

        public string ToStringHex()
        {
            return Tools.Hex(new byte[2] { Type, Value });
        }

        public override string ToString()
        {
            if (this == Absent)
                return "ABSENT";
            else
                return Tools.Hex1(Type) + " [" + Tools.Hex1(Value) + "]";
        }

        public override int GetHashCode()
        {
            return Type * 433 + Value;
        }

        public override bool Equals(object obj)
        {
            return obj is Effect effect && effect == this;
        }

        public static bool operator ==(Effect a, Effect b)
        {
            return a.Type == b.Type && a.Value == b.Value;
        }

        public static bool operator !=(Effect a, Effect b)
        {
            return a.Type != b.Type || a.Value != b.Value;
        }

        /// <summary>
        /// Represents an absent effect.
        /// </summary>
        public static readonly Effect Absent = new Effect(0xEF, 0xFF);
    }
}