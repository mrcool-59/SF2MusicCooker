using System;

namespace SF2MusicCooker.Furnace
{
    public readonly struct Position : IEquatable<Position>, IComparable<Position>
    {
        public readonly int Order;
        public readonly int Row;

        public Position(int order, int row)
        {
            Order = order;
            Row = row;
        }

        public int CompareTo(Position other)
        {
            if (Order == other.Order)
                return Row.CompareTo(other.Row);
            else
                return Order.CompareTo(other.Order);
        }

        public override string ToString()
        {
            return Order.ToString("X").PadLeft(2, '0') + ":" + Row.ToString("X").PadLeft(2, '0');
        }

        public override int GetHashCode()
        {
            return Order * 5939 + Row;
        }

        public bool Equals(Position other)
        {
            return other == this;
        }

        public override bool Equals(object obj)
        {
            return obj is Position position && position == this;
        }

        public static bool operator ==(Position a, Position b)
        {
            return a.Order == b.Order && a.Row == b.Row;
        }

        public static bool operator !=(Position a, Position b)
        {
            return a.Order != b.Order || a.Row != b.Row;
        }

        public static bool operator <(Position a, Position b)
        {
            return a.CompareTo(b) < 0;
        }

        public static bool operator >(Position a, Position b)
        {
            return a.CompareTo(b) > 0;
        }

        public static bool operator <=(Position a, Position b)
        {
            return a.CompareTo(b) <= 0;
        }

        public static bool operator >=(Position a, Position b)
        {
            return a.CompareTo(b) >= 0;
        }

        /// <summary>
        /// Represents the start position.
        /// </summary>
        public static readonly Position Start = new Position(0, 0);
    }
}