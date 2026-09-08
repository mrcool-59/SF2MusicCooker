using System;
using System.Text;

namespace SF2MusicCooker
{
    public sealed class Envelope : IEquatable<Envelope>
    {
        /// <summary>
        /// Level per tick during attack phase.
        /// </summary>
        public readonly byte[] Attack;

        /// <summary>
        /// Level per tick during release phase.
        /// </summary>
        public readonly byte[] Release;

        /// <summary>
        /// Total number of levels defined in the envelope (attack + release).
        /// </summary>
        public int Length { get { return Attack.Length + Release.Length; } }

        /// <summary>
        /// Read level value for the specified tick.
        /// </summary>
        public byte Read(int tick, bool released)
        {
            byte[] levels = released ? Release : Attack;
            return levels[Math.Max(0, Math.Min(levels.Length - 1, tick))];
        }

        public override int GetHashCode()
        {
            int H(byte[] array)
            {
                int h = array.Length;
                foreach (byte val in array) h = h * 17 + val;
                return h;
            }
            return H(Attack) + 314159 * H(Release);
        }

        public override bool Equals(object obj)
        {
            return obj is Envelope envelope && Equals(envelope);
        }

        public bool Equals(Envelope other)
        {
            return other != null && Equals(Attack, other.Attack) && Equals(Release, other.Release);
        }

        private static bool Equals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            void Append(byte[] values)
            {
                sb.Append('[');
                for (int i = 0; i < values.Length; i++)
                {
                    if (i != 0) sb.Append(", ");
                    sb.Append(values[i]);
                }
                sb.Append(']');
            }
            Append(Attack);
            sb.Append('-');
            Append(Release);
            return sb.ToString();
        }

        /// <summary>
        /// Combine the attack and release to a single array.
        /// </summary>
        public byte[] ToArray(int padAttack = 0, int padRelease = 0)
        {
            if (padAttack < 0) throw new ArgumentOutOfRangeException(nameof(padAttack), "must be zero or positive");
            if (padRelease < 0) throw new ArgumentOutOfRangeException(nameof(padRelease), "must be zero or positive");

            byte[] buffer = new byte[Length + padAttack + padRelease];
            int cursor = 0;
            for (int i = 0; i < Attack.Length; i++) buffer[cursor++] = Attack[i];
            for (int i = 0; i < padAttack; i++) buffer[cursor++] = Attack[Attack.Length - 1];
            for (int i = 0; i < Release.Length; i++) buffer[cursor++] = Release[i];
            for (int i = 0; i < padRelease; i++) buffer[cursor++] = Release[Release.Length - 1];
            return buffer;
        }

        public Envelope(byte[] attack, byte[] release)
        {
            if (attack == null || attack.Length == 0) throw new ArgumentException("cannot be null or empty", nameof(attack));
            if (release == null || release.Length == 0) throw new ArgumentException("cannot be null or empty", nameof(release));

            Attack = attack;
            Release = release;
        }

        /// <summary>
        /// The default envelope that does nothing.
        /// </summary>
        public static readonly Envelope Default = new Envelope(new byte[1] { 0xF }, new byte[1] { 0xF });
    }
}