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
            /// Indicate the length of the new note before release (0 if no note), this is *always* equal to or lower than note length.
            /// </summary>
            public readonly int NoteRelease;

            /// <summary>
            /// Indicate the length of the new note before stopping (0 if no note). Cannot be higher than 'maxPredictLength' specified in the Run method call.
            /// </summary>
            public readonly int NoteLength;

            /// <summary>
            /// Indicate the length of the silence (i.e: note OFF) before the next note (0 if no silence). Cannot be higher than 'maxPredictLength' specified in the Run method call.
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

            /// <summary>
            /// Print a warning to the user if note/silence length has reached the length threshold. Return true if warning was actually printed.
            /// </summary>
            public bool PrintLengthWarning(int lengthThreshold)
            {
                string what = null;
                if (SilenceLength >= lengthThreshold)
                {
                    what = "silence";
                    Console.WriteLine("! Silence triggered at [order: {0}, row: {1}] has a hit the maximum allowed length ({2} ticks)", Order, Row, SilenceLength);
                }
                if (NoteLength >= lengthThreshold)
                {
                    what = "note";
                    Console.WriteLine("! Note triggered at [order: {0}, row: {1}] has a hit the maximum allowed length ({2} ticks)", Order, Row, NoteLength);
                }
                if (what != null)
                {
                    Console.WriteLine("! This is either a bug in the tool or a deliberate, absurdly long {0} in the Furnace file.", what);
                    Console.WriteLine("! Regardless of the cause, the consequence is that the actual {0} length will be capped in the output sheet.", what);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Play each row of each pattern in the intended order and produce an enumeration of ticks.
        /// </summary>
        public static IEnumerable<Tick> Run(FurnaceFile file, int activeChannel, int maxPredictLength, int fromOrder = 0, int fromRow = 0)
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

                if (maxPredictLength > 0 && activeChannelCell != null)
                {
                    if (activeChannelCell.HasNewNote)
                    {
                        foreach (Tick tick in Run(file, activeChannel, 0, order, row))
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

                            // No need to go higher than max length
                            if (noteLength >= maxPredictLength) break;
                        }
                    }
                    else if (activeChannelCell.Note == PatternCell.NoteOff)
                    {
                        foreach (Tick tick in Run(file, activeChannel, 0, order, row))
                        {
                            // See how long the silence will last
                            if (tick.ActiveChannelCell.HasNewNote)
                                break;
                            else
                                silenceLength++;

                            // No need to go higher than max length
                            if (silenceLength >= maxPredictLength) break;
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
