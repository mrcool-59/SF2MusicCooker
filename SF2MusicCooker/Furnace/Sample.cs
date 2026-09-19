using System;

namespace SF2MusicCooker.Furnace
{
    public sealed class Sample
    {
        public readonly string Name;
        public readonly int Length;
        public readonly int Rate; // C-4
        public readonly byte Depth;
        public readonly byte LoopDirection;
        public readonly int LoopStart;
        public readonly int LoopEnd;
        public readonly byte[] Data;

        public Sample(string name, int length, int rate, byte depth, byte loopDirection, int loopStart, int loopEnd, byte[] data)
        {
            Name = name;
            Length = length;
            Rate = rate;
            Depth = depth;
            LoopDirection = loopDirection;
            LoopStart = loopStart;
            LoopEnd = loopEnd;
            Data = data;

            // HACK for bogus CubeAssets
            if (Depth == 16 && data.Length == length) Depth = 8;
        }

        public static void HalfwayShiftInPlace(byte[] data)
        {
            for (int i = 0; i < data.Length; i++) data[i] = (byte)(0x80 + data[i]);
        }

        private static byte[] ScalePCM(byte[] data, float coeff)
        {
            if (Math.Abs(1f - coeff) > 0.00001f)
            {
                data = (byte[])data.Clone();
                HalfwayShiftInPlace(data);
                for (int i = 0; i < data.Length; i++)
                {
                    float v = (data[i] - 128f) / 128f * coeff;
                    data[i] = (byte)Math.Max(sbyte.MinValue, Math.Min(sbyte.MaxValue, 0x80 + Math.Round(v * 128f)));
                }
                HalfwayShiftInPlace(data);
            }
            return data;
        }

        public Sample Multiply(float rateCoeff, float volumeCoeff)
        {
            int newRate = (int)Math.Round(Rate * rateCoeff);
            byte[] newData = ScalePCM(Data, volumeCoeff);

            if (newRate != Rate || newData != Data)
                return new Sample(Name, Length, newRate, Depth, LoopDirection, LoopStart, LoopEnd, newData);
            else
                return this;
        }

        public void Verify()
        {
            if (LoopStart != -1 || LoopEnd != -1)
                throw new NotSupportedException("Sorry, samples may not loop (" + Name + ")");

            if (Depth != 0x08)
                throw new NotSupportedException("8-bit PCM is the expected format for samples");

            if (Length != Data.Length)
                throw new FormatException("Data length is not consistent with declared length");
        }

        /// <summary>
        /// The muted sample that has the smallest size and produces no sound.
        /// </summary>
        public static readonly Sample Muted = new Sample("-Muted-", 1, 5000, 0x08, 0, -1, -1, new byte[1] { 0x00 });
    }
}