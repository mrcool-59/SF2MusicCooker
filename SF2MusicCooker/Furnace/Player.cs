using System;
using System.Collections.Generic;

namespace SF2MusicCooker.Furnace
{
    public static class Player
    {
        /// <summary>
        /// Play each row of each pattern in the intended order and produce an enumeration of ticks.
        /// </summary>
        public static IEnumerable<Tick> Run(FurnaceFile file, int activeChannel, int maxPredictLength, Position from)
        {
            int channels = file.Channels;
            int orders = file.Orders;
            int rows = file.Rows;

            int order = from.Order;
            int row = from.Row;

            byte shape = 0x00;
            VibratoState vibratoState = new VibratoState();
            HashSet<Position> vibratoTaken = new HashSet<Position>();

            void ApplyVibrato(Effect vibrato, int vibratoDelay)
            {
                byte speed = (byte)(vibrato.Value >> 4);
                byte depth = (byte)(vibrato.Value & 0x0F);
                byte delay = (byte)Math.Min(0xFF, vibratoDelay);

                vibratoState = new VibratoState(shape, speed, depth, delay);
            }

            while (order < orders)
            {
                Position position = new Position(order, row);
                Effect end = Effect.Absent;
                Effect goTo = Effect.Absent;
                Effect goNext = Effect.Absent;
                PatternCell activeChannelCell = null;

                for (int channel = 0; channel < channels; channel++)
                {
                    int key = file.KeyByChannelAndOrder[channel, order];
                    var pattern = file.PatternByKey[key];
                    var cell = pattern.Get(row);

                    if (channel == activeChannel)
                        activeChannelCell = cell;

                    if (end == Effect.Absent) cell.TryGetEffect(Effect.End, out end);
                    if (goTo == Effect.Absent) cell.TryGetEffect(Effect.GoTo, out goTo);
                    if (goNext == Effect.Absent) cell.TryGetEffect(Effect.GoNext, out goNext);
                }

                // Figure out the length of the note or silence by predicting the future
                int noteRelease = 0;
                int noteLength = 0;
                int silenceLength = 0;

                if (maxPredictLength > 0 && activeChannelCell != null)
                {
                    if (activeChannelCell.TryGetEffect(Effect.VibratoShape, out Effect effect))
                    {
                        shape = effect.Value;
                    }

                    if (activeChannelCell.HasNewNote)
                    {
                        // Figure out if and when a new vibrato should be applied
                        int vibratoDelay = 0;

                        foreach (Tick tick in Run(file, activeChannel, 0, position))
                        {
                            var cell = tick.ActiveChannelCell;

                            // Take the first vibrato effect encountered and memorize its delay
                            if ((noteLength == 0 || !cell.HasNewNote) && vibratoDelay >= 0)
                            {
                                if (cell.TryGetEffect(Effect.Vibrato, out Effect vibrato))
                                {
                                    ApplyVibrato(vibrato, vibratoDelay);
                                    vibratoTaken.Add(tick.Position);
                                    vibratoDelay = -1;
                                }
                                else
                                {
                                    vibratoDelay++;
                                }
                            }

                            // Note must always last 1 tick at bare minimum!
                            if (noteLength == 0)
                            {
                                noteRelease++;
                                noteLength++;
                                continue;
                            }

                            // See how long before the note is released and ends
                            if (cell.Note == PatternCell.NoteAbsent)
                            {
                                if (noteRelease == noteLength) noteRelease++; // No longer increment if key was released!
                                noteLength++;
                            }
                            else if (cell.Note == PatternCell.NoteRelease)
                            {
                                noteLength++;
                            }
                            else
                            {
                                break;
                            }

                            // No need to go higher than max length
                            if (noteLength >= maxPredictLength) break;
                        }
                    }
                    else if (activeChannelCell.Note == PatternCell.NoteOff)
                    {
                        foreach (Tick tick in Run(file, activeChannel, 0, position))
                        {
                            if (silenceLength == 0)
                            {
                                // Silence must always last 1 tick at bare minimum!
                                silenceLength++;
                                continue;
                            }

                            // See how long the silence will last
                            var cell = tick.ActiveChannelCell;
                            if (cell.HasNewNote || cell.Note == PatternCell.NoteOff)
                                break;
                            else
                                silenceLength++;

                            // No need to go higher than max length
                            if (silenceLength >= maxPredictLength) break;
                        }
                    }

                    if (activeChannelCell.TryGetEffect(Effect.Vibrato, out Effect extVibrato) && !vibratoTaken.Contains(position))
                    {
                        ApplyVibrato(extVibrato, 0); // For vibrato outside of playing note
                    }
                }

                // Set next position using the appropriate control flow (based on Furnace default logic -- no compatibility flag!)
                if (end != Effect.Absent)
                {
                    row = 0;
                    order = orders;
                }
                else if (goTo != Effect.Absent && goNext != Effect.Absent)
                {
                    row = Math.Min(rows - 1, goNext.Value); // If row overflows, clamp to last valid row
                    order = goTo.Value >= orders ? 0 : goTo.Value; // If order overflows, we go to order 0 instead
                }
                else if (goTo != Effect.Absent)
                {
                    row = 0;
                    order = goTo.Value >= orders ? 0 : goTo.Value; // If order overflows, we go to order 0 instead
                }
                else if (goNext != Effect.Absent)
                {
                    row = Math.Min(rows - 1, goNext.Value); // If row overflows, clamp to last valid row
                    order++;
                    if (order == orders) order = 0; // If order overflows, we go to order 0 instead
                }
                else if (++row == rows)
                {
                    row = 0;
                    order++;
                }

                // Submit to caller
                Position nextPosition = new Position(order, row);
                yield return new Tick(position, nextPosition, activeChannelCell, noteRelease, noteLength, silenceLength, vibratoState);
            }
        }
    }
}