using System;
using System.Collections.Generic;
using static SF2MusicCooker.FurnaceFile;

namespace SF2MusicCooker
{
    public static class FurnacePlayer
    {
        public readonly struct Tick
        {
            /// <summary>
            /// Current order in the playback.
            /// </summary>
            public readonly int Order;

            /// <summary>
            /// Current row in the playback.
            /// </summary>
            public readonly int Row;

            /// <summary>
            /// The currently playing cell for the active channel.
            /// </summary>
            public readonly PatternCell ActiveChannelCell;

            /// <summary>
            /// Indicate the length of the new note before release (0 if no note).
            /// </summary>
            public readonly int NoteRelease;

            /// <summary>
            /// Indicate the length of the new note before stopping (0 if no note).
            /// </summary>
            public readonly int NoteLength;

            /// <summary>
            /// Indicate the length of the silence (i.e: note OFF) before the next note (0 if no silence).
            /// </summary>
            public readonly int SilenceLength;

            /// <summary>
            /// Filled if we encounter a 'go to pattern' effect that sends us backward.
            /// In that case, this field contains the order value to go backward to.
            /// Otherwise this value will be -1.
            /// </summary>
            public readonly int BackwardGoToOrder;

            public Tick(int order, int row, PatternCell activeChannelCell, int noteRelease, int noteLength, int silenceLength, int backwardGoToOrder = -1)
            {
                Order = order;
                Row = row;
                ActiveChannelCell = activeChannelCell;
                NoteRelease = noteRelease;
                NoteLength = noteLength;
                SilenceLength = silenceLength;
                BackwardGoToOrder = backwardGoToOrder;
            }
        }

        /// <summary>
        /// Play each row of each pattern in the intended order and produce an enumeration of ticks.
        /// </summary>
        public static IEnumerable<Tick> Run(FurnaceFile file, int activeChannel)
        {
            return Run(file, activeChannel, 0, 0, true); // Call the private method without exposing the forbidden parameters
        }

        private static IEnumerable<Tick> Run(FurnaceFile file, int activeChannel, int fromOrder, int fromRow, bool predictLength)
        {
            if (file.Orders == 0) yield break;

            int channels = file.Channels;
            int orders = file.Orders;
            int rows = file.Rows;

            int order = fromOrder;
            int row = fromRow;

            while (order < orders)
            {
                Effect goTo = Effect.Absent;
                Effect goNext = Effect.Absent;
                Effect end = Effect.Absent;
                PatternCell activeChannelCell = null;
                int backwardGoToOrder = -1;

                for (int channel = 0; channel < channels; channel++)
                {
                    int key = file.KeyByChannelAndOrder[channel, order];
                    var pattern = file.PatternByKey[key];
                    var cell = pattern.Get(row);

                    if (channel == activeChannel)
                        activeChannelCell = cell;

                    if (goTo == Effect.Absent && cell.TryGetEffect(Effect.GoTo, out goTo))
                    {
                        if (goTo.Value < 0 || goTo.Value >= file.Orders) throw new FormatException("Encountered 'go to pattern' effect (0B) with invalid order value");
                        if (goTo.Value <= order) backwardGoToOrder = goTo.Value;
                    }

                    if (goNext == Effect.Absent)
                        _ = cell.TryGetEffect(Effect.GoNext, out goNext);

                    if (end == Effect.Absent)
                        _ = cell.TryGetEffect(Effect.End, out end);
                }

                // Figure out the length of the note or silence by predicting the future
                int noteRelease = 0;
                int noteLength = 0;
                int silenceLength = 0;

                if (predictLength && activeChannelCell != null)
                {
                    if (activeChannelCell.HasNewNote)
                    {
                        foreach (Tick tick in Run(file, activeChannel, order, row, false))
                        {
                            if (noteLength == 0)
                            {
                                // Note must always last 1 tick at bare minimum!
                                noteRelease++;
                                noteLength++;
                                continue;
                            }

                            // See how long before the note is released and ends
                            var cell = tick.ActiveChannelCell;
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

                            // Safety net
                            if (noteLength >= 4096) throw new ApplicationException("Sorry, we got a note with predicted length >= 4096 which means it is probably a bug in the tool :(");
                        }
                    }
                    else if (activeChannelCell.Note == PatternCell.NoteOff)
                    {
                        foreach (Tick tick in Run(file, activeChannel, order, row, false))
                        {
                            // See how long the silence will last
                            if (tick.ActiveChannelCell.HasNewNote)
                                break;
                            else
                                silenceLength++;

                            // Safety net
                            if (silenceLength >= 4096) throw new ApplicationException("Sorry, we got a silence with predicted length >= 4096 which means it is probably a bug in the tool :(");
                        }
                    }
                }

                // Submit to caller
                yield return new Tick(order, row, activeChannelCell, noteRelease, noteLength, silenceLength, backwardGoToOrder);

                // Go to the next step using the appropriate control flow
                if (goTo != Effect.Absent)
                {
                    row = 0;
                    order = goTo.Value;
                }
                else if (goNext != Effect.Absent)
                {
                    row = 0;
                    order++;
                }
                else if (end != Effect.Absent)
                {
                    yield break; // End the song
                }
                else if (++row == rows)
                {
                    row = 0;
                    order++;
                }
            }
        }
    }
}
