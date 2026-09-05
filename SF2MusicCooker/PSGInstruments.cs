using SF2MusicCooker.Furnace;
using System;
using System.Linq;

namespace SF2MusicCooker
{
    public static class PSGInstruments
    {
        /// <summary>
        /// Given the supplied hints, figure out the PSG instrument to use (volume envelope + total level).
        /// </summary>
        public static byte Guess(FurnaceFile file, Tick tick, int channel, ushort furnaceInstrument, byte totalLevel)
        {
            byte[] levels = null;

            if (furnaceInstrument < file.Instruments.Length)
            {
                Instrument instrument = file.Instruments[furnaceInstrument];
                if (instrument.Type == Instrument.PSG && instrument.Data != null)
                    levels = FeatureInterpreter.ParseFurnacePSGMacroLevels(instrument.Data);
            }

            if (levels == null)
                levels = ReadLevels(file, channel, tick.Position, tick.NoteLength, totalLevel);

            byte envelope = GuessEnvelope(levels);

            return (byte)((envelope << 4) | totalLevel);
        }

        private static byte[] ReadLevels(FurnaceFile file, int channel, Position position, int length, byte initialLevel)
        {
            byte currentLevel = initialLevel;
            byte[] levels = new byte[length];
            int i = 0;
            foreach (Tick tick in Player.Run(file, channel, 0, position))
            {
                PatternCell cell = tick.ActiveChannelCell;
                if (cell.Volume != PatternCell.VolumeAbsent) currentLevel = cell.Volume;

                levels[i++] = currentLevel;
                if (i >= length) break;
            }
            Normalize(levels);
            return levels;
        }

        private static void Normalize(byte[] levels)
        {
            byte max = levels.Max();
            byte min = levels.Min();

            if (max != min)
            {
                for (int i = 0; i < levels.Length; i++)
                {
                    float normalized = (float)(levels[i] - min) / (max - min);
                    levels[i] = (byte)Math.Round(normalized * 0xF);
                }
            }
            else
            {
                Tools.Fill(levels, 0, levels.Length, 0x0F);
            }
        }

        private static byte GuessEnvelope(byte[] levels)
        {
            return 0x00; // TODO
        }
    }
}
