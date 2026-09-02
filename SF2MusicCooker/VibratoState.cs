namespace SF2MusicCooker
{
    public readonly struct VibratoState
    {
        public readonly byte Shape;
        public readonly byte Speed;
        public readonly byte Depth;
        public readonly byte Delay;

        public bool Active { get { return Speed > 0 && Depth > 0; } }

        public VibratoState(byte shape, byte speed, byte depth, byte delay)
        {
            Shape = shape;
            Speed = speed;
            Depth = depth;
            Delay = delay;
        }

        public override string ToString()
        {
            return Tools.Hex(new byte[4] { Shape, Speed, Depth, Depth });
        }
    }
}