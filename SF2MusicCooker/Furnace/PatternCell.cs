using System;
using System.Collections.Generic;
using System.Text;

namespace SF2MusicCooker.Furnace
{
    public sealed class PatternCell
    {
        public const byte NoteCMinus5 = 0;
        public const byte NoteB9 = 179;
        public const byte NoteOff = 180;
        public const byte NoteRelease = 181;
        // public const byte MacroRelease = 182;
        public const byte NoteAbsent = 255;
        public const ushort InstrumentAbsent = 65535;
        public const byte VolumeAbsent = 255;

        public readonly byte Note;
        public readonly ushort Instrument;
        public readonly byte Volume;
        public readonly Effect[] Effects;

        /// <summary>
        /// True if the cell contains nothing.
        /// </summary>
        public bool IsEmpty { get { return Note == NoteAbsent && Instrument == InstrumentAbsent && Volume == VolumeAbsent && Effects.Length == 0; } }

        /// <summary>
        /// True if the cell contains a new note to play.
        /// </summary>
        public bool HasNewNote { get { return Note != NoteAbsent && Note != NoteRelease && Note != NoteOff; } }

        public PatternCell()
        {
            Note = NoteAbsent;
            Instrument = InstrumentAbsent;
            Volume = VolumeAbsent;
            Effects = Array.Empty<Effect>();
        }

        public PatternCell(byte note, ushort instrument, byte volume, Effect[] effects)
        {
            Note = note;
            Instrument = instrument;
            Volume = volume;
            Effects = effects ?? Array.Empty<Effect>();
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(GetNoteString(Note));
            sb.Append(Instrument == InstrumentAbsent ? ".." : Tools.Hex2(Instrument, false));
            sb.Append(Volume == VolumeAbsent ? ".." : Tools.Hex1(Volume));
            foreach (Effect effect in Effects) sb.Append(effect.ToStringHex());
            return sb.ToString();
        }

        /// <summary>
        /// Get a user-friendly representation of a note command.
        /// </summary>
        public static string GetNoteString(byte note)
        {
            if (note == NoteAbsent)
                return "...";
            else if (note == NoteOff)
                return "OFF";
            else if (note == NoteRelease)
                return "===";
            else
                return NoteBible.GetByValue(note).Name;
        }

        /// <summary>
        /// Get the first effect of the provided type in the cell and return true on success.
        /// Otherwise return false and write the "Absent" effect.
        /// </summary>
        public bool TryGetEffect(byte type, out Effect effect)
        {
            foreach (Effect e in Effects)
            {
                if (e.Type == type)
                {
                    effect = e;
                    return true;
                }
            }
            effect = Effect.Absent;
            return false;
        }

        /// <summary>
        /// Multiply pattern cell by N and return the resulting cell as well as the last padding cell (that will hold effects related to navigation).
        /// This method only does something when a time effect is present on the pattern cell.
        /// </summary>
        public PatternCell Multiply(int n, out PatternCell beforeNextCell)
        {
            beforeNextCell = null;
            if (Effects.Length > 0)
            {
                List<Effect> remainingEffects = new List<Effect>(Effects.Length);
                List<Effect> delayedEffects = new List<Effect>(0);
                foreach (Effect effect in Effects)
                {
                    switch (effect.Type)
                    {
                        case Effect.GoNext:
                            delayedEffects.Add(new Effect(effect.Type, (byte)Math.Min(0xFF, effect.Value * n)));
                            break;
                        case Effect.GoTo:
                        case Effect.End:
                            delayedEffects.Add(effect);
                            break;
                        default:
                            // TODO: setTickRate
                            remainingEffects.Add(effect);
                            break;
                    }
                }
                if (delayedEffects.Count > 0)
                {
                    beforeNextCell = new PatternCell(NoteAbsent, InstrumentAbsent, VolumeAbsent, delayedEffects.ToArray());
                }
                return new PatternCell(Note, Instrument, Volume, remainingEffects.ToArray());
            }
            return this;
        }

        /// <summary>
        /// Represents the empty cell.
        /// </summary>
        public static readonly PatternCell Empty = new PatternCell();
    }
}