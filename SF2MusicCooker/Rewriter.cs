using SF2MusicCooker.Furnace;

namespace SF2MusicCooker
{
    public static class Rewriter
    {
        public static void Execute(FurnaceFile file, bool enabled)
        {
            if (!enabled) return;

            file.Calculate();

            for (int channel = 0; channel < file.Channels; channel++)
            {
                Execute(file, channel);
            }
        }

        private static void Execute(FurnaceFile file, int channel)
        {
            if (!file.HasNote(channel)) return;

            Loop loop = file.Loop;
            PatternCell lastNoteCell = null;
            Position lastNotePosition = new Position();

            foreach (Tick tick in Player.Run(file, channel, 0, Position.Start))
            {
                Position position = tick.Position;
                PatternCell cell = tick.ActiveChannelCell;

                if (cell.HasNewNote)
                {
                    lastNoteCell = cell;
                    lastNotePosition = tick.Position;
                }
                else if (cell.Note == PatternCell.NoteRelease || cell.Note == PatternCell.NoteOff)
                {
                    lastNoteCell = null;
                }
                else if (ShouldRewrite(cell) && lastNoteCell != null)
                {
                    // The previous note must have a single tick legato effect attached
                    if (!lastNoteCell.TryGetEffect(Effect.LegatoSingleTick, out Effect effect) || effect.Value == 0x00)
                    {
                        Effect[] adjustedEffects = AppendEffect(lastNoteCell.Effects, new Effect(Effect.LegatoSingleTick, 0x01));
                        PatternCell newLastNoteCell = new PatternCell(lastNoteCell.Note, lastNoteCell.Instrument, lastNoteCell.Volume, adjustedEffects);
                        file.PatternByKey[file.KeyByChannelAndOrder[channel, lastNotePosition.Order]].Set(lastNotePosition.Row, newLastNoteCell);
                    }

                    PatternCell newCell = new PatternCell(lastNoteCell.Note, cell.Instrument, cell.Volume, cell.Effects);
                    file.PatternByKey[file.KeyByChannelAndOrder[channel, position.Order]].Set(position.Row, newCell);
                }
                if (tick.NextPosition == loop.Start && tick.Position == loop.End) break;
            }
        }

        private static Effect[] AppendEffect(Effect[] array, Effect effect)
        {
            Effect[] effects = new Effect[array.Length + 1];
            for (int i = 0; i < array.Length; i++) effects[i] = array[i];
            effects[array.Length] = effect;
            return effects;
        }

        private static bool ShouldRewrite(PatternCell cell)
        {
            return cell.Volume != PatternCell.VolumeAbsent || cell.TryGetEffect(Effect.Pan, out _) || cell.TryGetEffect(Effect.PanTrinary, out _) || cell.TryGetEffect(Effect.Detune, out _);
        }
    }
}
