using SF2MusicCooker.Furnace;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace SF2MusicCooker
{
    public sealed class ChannelCommands
    {
        private readonly string[] _channels;
        private readonly uint _mask;
        private readonly bool _requiresDAC;
        private readonly string _separator;

        public const uint Mask_Music = 0b1111111111;
        public const uint Mask_SFX_1 = 0b0000000011;
        public const uint Mask_SFX_2 = 0b0001110000;

        /// <summary>
        /// Get the DAC value to write in music header.
        /// </summary>
        public byte DAC { get; private set; }

        /// <summary>
        /// Get if the generated music is empty.
        /// </summary>
        public bool Empty { get; private set; }

        public ChannelCommands(int channels, uint mask, bool requiresDAC, string separator)
        {
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels), "cannot be zero or negative");
            if (channels > 31) throw new ArgumentOutOfRangeException(nameof(channels), "cannot be higher than 31");

            if (channels != 10)
                throw new NotSupportedException("This tool can only handle Sega Genesis music, with 10 channels (6 FM + 4 PSG)");

            _channels = new string[channels];
            _mask = mask;
            _requiresDAC = requiresDAC;
            _separator = separator ?? throw new ArgumentNullException(nameof(separator));

            Empty = true;
        }

        private int FindData(string data)
        {
            for (int channel = 0; channel < _channels.Length; channel++)
            {
                int index = _channels.Length - channel - 1;
                if (_channels[index] == data) return index;
            }
            return -1;
        }

        /// <summary>
        /// Write generated channel pointers and data in the specified string builder.
        /// </summary>
        public void Write(StringBuilder sb, string prefix, string padding)
        {
            // Used to recycle identical channel data
            bool[] used = new bool[_channels.Length];

            // Write pointers
            for (int channel = 0; channel < _channels.Length; channel++)
            {
                if (_channels[channel] != null)
                {
                    int index = FindData(_channels[channel]);
                    Debug.Assert(index >= 0);

                    used[index] = true;
                    sb.Append(padding);
                    sb.AppendFormat("dw {0}_Channel_{1}", prefix, index); sb.AppendLine();
                }
            }
            sb.AppendLine();

            // Write data
            for (int index = 0; index < used.Length; index++)
            {
                if (used[index])
                {
                    sb.AppendFormat("{0}_Channel_{1}:", prefix, index); sb.AppendLine();
                    sb.Append(padding);
                    sb.Append(_channels[index]);
                    sb.AppendLine();
                }
            }
        }

        /// <summary>
        /// Generate channel commands from a Furnace file and the provided instrument map.
        /// </summary>
        public void Generate(FurnaceFile file, Options options, InstrumentMap map)
        {
            if (file.Channels != _channels.Length)
                throw new NotSupportedException("Furnace file must have " + _channels.Length + " channels"); // 6 from YM2612 + 4 from PSG

            // Check all instruments are used properly and indicate if DAC mode is enabled
            map.Check(file, _requiresDAC, out byte dac);
            DAC = dac;

            // Compute loop position
            Position loopStart = FindLoopStart(file, true);

            // Fill each channel
            for (int channel = 0; channel < file.Channels; channel++)
            {
                _channels[channel] = GenerateChannel(file, options, map, channel, loopStart);
            }

            // Update empty flag
            Empty = Array.TrueForAll(_channels, c => c == null || c == "channel_end");
        }

        private string GenerateChannel(FurnaceFile file, Options options, InstrumentMap map, int channel, Position loopStart)
        {
            // Mask verification
            if ((_mask & (1 << channel)) == 0) return null;

            // Do not generate channels that are empty or muted
            if (!file.HasPlayNoteCommand(channel) || (options.IsolateChannel >= 0 && channel != options.IsolateChannel)) return "channel_end";

            // We must be able to reach the first note
            int firstNoteTicks = FindFirstNote(file, channel, true);
            if (firstNoteTicks == 0) return "channel_end";

            // Volume to cube helper
            Volume volume = Volume.FromStrategy(options.VolumeStrategy);
            byte VOL_F2C(byte ymVolume) => volume.Y2C(ymVolume);

            // Prepare state
            List<string> commands = new List<string>(file.Orders * file.Rows); // Rough estimate of the needed capacity
            bool instrumentChanged = true;
            byte currentInstrument = 0x00;
            byte currentVolume = VOL_F2C(0x7F); // Max volume by default
            byte currentPan = PAN_F2C(0x00);
            byte currentRelease = 0xFF;
            byte currentLength = 0x00;

            // Is it FM or PSG?
            bool fm = channel < 6;

            // Write initial pan for FM channels
            if (fm) commands.Add("stereo " + BYTE_HEX(currentPan)); // TODO: will be done by first note, just set a flag to write it

            // Cancel vibrato immediately (looks like when the game boots a vibrato is set, at least in test mode)
            commands.Add("vibrato " + BYTE(0));

            // Cancel shifting immediately (to be safe)
            // commands.Add("shifting " + BYTE(0));

            // Write initial silence (before the first note)
            WriteSilence(firstNoteTicks - 1);

            // We assume notes should never be longer than this very generous length
            int maxPredictLength = file.Orders * file.Rows * 16;

            // Play the song while observing this channel in particular
            int ticks = 0;
            foreach (Tick tick in Player.Run(file, channel, maxPredictLength, Position.Start))
            {
                PatternCell cell = tick.ActiveChannelCell;
                ticks++;

                // Verify that note/silence length we got is not absurd
                _ = tick.PrintLengthWarning(maxPredictLength);

                // Mark beginning of loop
                if (tick.Position == loopStart)
                {
                    commands.Add("mainLoopStart");
                }

                // Is it FM or PSG channel?
                if (fm)
                {
                    // For troubleshooting
                    if (options.DumpNotes) commands.Add("; " + cell);

                    // Apply pan change (we support 2 possible ways to define pan change in Furnace)
                    WritePan(cell);

                    // Defer instrument change
                    if (cell.Instrument != currentInstrument && cell.Instrument != PatternCell.InstrumentAbsent)
                    {
                        currentInstrument = cell.Instrument;
                        instrumentChanged = true;
                    }

                    // Apply volume change (with exceptions)
                    if (cell.Volume != PatternCell.VolumeAbsent && currentVolume != VOL_F2C(cell.Volume))
                    {
                        currentVolume = VOL_F2C(cell.Volume);
                        // Do not write volume command before the first note has played
                        // Do not write volume command if it's going to be replaced immediately after an instrument change
                        if (ticks >= firstNoteTicks && (!cell.HasNewNote || !instrumentChanged)) commands.Add("vol " + BYTE(currentVolume));
                    }

                    // Apply note change
                    if (cell.HasNewNote)
                    {
                        // Prove that we determine correctly the note release / length
                        PrintLength(commands, tick, false);

                        // Apply instrument change
                        if (instrumentChanged)
                        {
                            instrumentChanged = false;
                            if (map.FM(currentInstrument, out byte inst))
                            {
                                commands.Add("inst " + BYTE(inst));
                                commands.Add("vol " + BYTE(currentVolume)); // We output a volume change instruction after an instrument change
                            }
                        }

                        // TODO: sample / sampleL commands

                        // Finally, write the note itself
                        WriteNote(cell.Note, tick.NoteRelease, tick.NoteLength);
                    }
                    else if (cell.Note == PatternCell.NoteOff && ticks >= firstNoteTicks)
                    {
                        // Prove that we determine correctly the silence length
                        PrintLength(commands, tick, true);

                        // Please notice that note OFF commands are ignored if the first note hasn't been played yet
                        WriteSilence(tick.SilenceLength);
                    }

                    // TODO: remaining FM effects to implement/review (see below)
                }
                else
                {
                    // PSG channel grammar
                    // TODO
                }

                if (tick.NextPosition <= tick.Position)
                {
                    // Mark ending of loop and exit
                    commands.Add("mainLoopEnd");
                    break;
                }
                else if (tick.NextPosition >= file.End)
                {
                    // We reached the end (can only occur if there's no loop)
                    commands.Add("channel_end");
                    break;
                }
            }

            // List to array
            string[] finalCommands = commands.ToArray();

            // Apply optimization (only if dumping is disabled)
            if (options.OptimizeNotes && !options.DumpNotes) finalCommands = optimizer.Optimize(finalCommands);

            // Get the assembly
            string asm = string.Join(_separator, finalCommands);

            // Remove empty loops (can happen if we play 1 note that we infinitely sustain in a loop that does nothing)
            asm = asm.Replace("mainLoopStart" + _separator + "mainLoopEnd", "channel_end");

            // Channel done!
            return asm;

            // ------------------------------ Helpers -------------------------------

            void WriteNote(byte note, int release, int length)
            {
                int cappedLength = Math.Min(length, release + 0x7F); // After releasing a note, we can't have it play for more than 0x7F ticks
                int extraSilence = length - cappedLength;

                length = cappedLength;

                while (length > 0)
                {
                    if (length >= 0x100)
                        WriteSetReleaseOrSustain(-1); // This note is sustained because it goes above the max length of a single note (i.e: another note is required)
                    else
                        WriteSetReleaseOrSustain(length - release); // This note will end, we can also set when it should be released

                    byte newLength = (byte)Math.Min(0xFF, length);
                    if (currentLength != newLength)
                    {
                        currentLength = newLength;
                        commands.Add("noteL " + NOTE(note) + "," + BYTE(currentLength));
                    }
                    else
                    {
                        commands.Add("note  " + NOTE(note));
                    }
                    release -= newLength;
                    length -= newLength;
                }

                WriteSilence(extraSilence);
            }

            void WriteSetReleaseOrSustain(int length) // -1: never released (sustain), 0: released at note length, N: released at (note length - N)
            {
                byte newRelease = length < 0 ? (byte)0x80 : (byte)Math.Min(0x7F, length);
                if (currentRelease != newRelease)
                {
                    currentRelease = newRelease;
                    if (currentRelease == 0x80)
                        commands.Add("sustain"); // 'setRelease 80h' would be equivalent, but less clear
                    else
                        commands.Add("setRelease " + BYTE(currentRelease));
                }
            }

            void WriteSilence(int length)
            {
                while (length > 0)
                {
                    byte newLength = (byte)Math.Min(0xFF, length);
                    if (currentLength != newLength)
                    {
                        currentLength = newLength;
                        commands.Add("waitL " + BYTE(currentLength));
                    }
                    else
                    {
                        commands.Add("wait");
                    }
                    length -= newLength;
                }
            }

            void WritePan(PatternCell cell)
            {
                if (cell.TryGetEffect(Effect.Pan, out Effect pan))
                {
                    byte newPan = PAN_F2C(pan.Value);
                    if (currentPan != newPan)
                    {
                        currentPan = newPan;
                        commands.Add("stereo " + BYTE_HEX(currentPan));
                    }
                }
                else if (cell.TryGetEffect(Effect.PanTrinary, out pan))
                {
                    byte newPan = PAN3_F2C(pan.Value);
                    if (currentPan != newPan)
                    {
                        currentPan = newPan;
                        commands.Add("stereo " + BYTE_HEX(currentPan));
                    }
                }
            }
        }

        [Conditional("PRINT_LENGTH")]
        private static void PrintLength(List<string> commands, Tick tick, bool silence)
        {
            if (silence)
            {
                commands.Add("; Silence length = " + tick.SilenceLength);
            }
            else
            {
                commands.Add("; Note release in = " + tick.NoteRelease);
                commands.Add("; Note length = " + tick.NoteLength);
            }
        }

        private static Position FindLoopStart(FurnaceFile file, bool print)
        {
            Position loopStart = file.End;
            Position loopEnd = loopStart;
            int ticks = 0;
            foreach (Tick tick in Player.Run(file, -1, 0, Position.Start))
            {
                ticks++;
                if (tick.NextPosition <= tick.Position)
                {
                    loopStart = tick.NextPosition;
                    loopEnd = tick.Position;
                    break;
                }
            }
            if (print && ticks > 0)
            {
                Console.WriteLine("> Executed {0} ticks before ending playback", ticks);
                if (loopStart != file.End)
                    Console.WriteLine("> Music contains a loop: {0} -> {1}", loopEnd, loopStart);
                else
                    Console.WriteLine("> Music doesn't contain a loop");
            }
            return loopStart;
        }

        private static int FindFirstNote(FurnaceFile file, int channel, bool print)
        {
            int firstNoteTicks = 0;
            int ticks = 0;
            foreach (Tick tick in Player.Run(file, channel, 0, Position.Start))
            {
                ticks++;
                if (tick.ActiveChannelCell.HasNewNote)
                {
                    firstNoteTicks = ticks;
                    break;
                }
                else if (tick.NextPosition <= tick.Position)
                {
                    break;
                }
            }
            if (print && ticks > 0)
            {
                if (firstNoteTicks > 0)
                    Console.WriteLine("> Channel {0} first note tick: {1}", channel, ticks);
                else
                    Console.WriteLine("! Channel {0} first note never happens", channel);
            }
            return firstNoteTicks;
        }

        private static readonly LoopOptimizer optimizer = new LoopOptimizer(repeats => "countedLoopStart " + (repeats - 1), _ => "countedLoopEnd",
            7, 512, int.MaxValue, 32, Tools.RemoveASMComment, null, Weighter);

        private static int Weighter(string commandAndArguments)
        {
            string command = AsmSheetToolkit.StripArguments(commandAndArguments);
            int size = AsmSheetToolkit.GetCommandSize(command);
            // We definitely don't want unknown or main loop commands to be part of any loop whatsoever
            if (size == 0 || command == "mainLoopStart" || command == "mainLoopEnd") return short.MinValue;
            return size;
        }

        private static string BYTE(byte value)
        {
            return value.ToString();
        }

        private static string BYTE_HEX(byte value)
        {
            return Tools.Hex1ASM(value);
        }

        private static string NOTE(byte value)
        {
            // In "The First Battle" test music:
            // With +0 value offset: instruments do not play at the correct height
            // With +12 value offset: instruments sound correct but I can't shake the feeling this is way too brittle

            // TODO: This requires a more rigorous approach to get the correct notes all the time
            // Take a look at Furnace source code to figure how the frequency register is filled
            // Then select the closest available frequency in the Cube catalog
            // Also don't forget to do something like this:
            //      value = (byte)Math.Max(0, Math.Min(0x53, value)); // Cube sound engine has 0x54 notes defined [0..0x53]

            return NoteBible.GetByValue((byte)(value + 12)).Label; // Verify the note is valid/supported and return the proper ASM label
        }

        private static byte PAN_F2C(byte pan)
        {
            bool disableLeft = (pan & 0x0F) != 0;
            bool disableRight = (pan & 0xF0) != 0;
            if (disableLeft && disableRight) { disableLeft = disableRight = false; }
            return (byte)((disableLeft ? 0 : (1 << 7)) | (disableRight ? 0 : (1 << 6)));
        }

        private static byte PAN3_F2C(byte trinaryPan)
        {
            if (trinaryPan == 0x00) return 1 << 6; // LEFT
            else if (trinaryPan == 0xFF) return 1 << 7; // RIGHT
            else return (1 << 6) | (1 << 7); // CENTER (including invalid values)
        }
    }
}

/* -------------------- AVAILABLE GRAMMAR --------------------

For all (timing/looping):
                commands.Add("mainLoopStart");
                commands.Add("mainLoopEnd");
                commands.Add("wait");
                commands.Add("waitL " + BYTE(192));

For FM/DAC channels:
                commands.Add("stereo " + BYTE_HEX(0xC0));
                commands.Add("vol " + BYTE_HEX(0x0C));

For FM channels:
                commands.Add("inst " + BYTE(0));
                commands.Add("sustain");
                commands.Add("setRelease " + BYTE_HEX(0x05));
                commands.Add("vibrato " + BYTE_HEX(0x2C));
                commands.Add("setSlide " + BYTE_HEX(0x20));
                commands.Add("noSlide");
                commands.Add("shifting " + BYTE_HEX(0x20));
                commands.Add("noteL " + NOTE(0x51) + "," + BYTE(24));
                commands.Add("note " + NOTE(0x51));

For DAC channel:
                commands.Add("sample " + BYTE(4));
                commands.Add("sampleL " + BYTE(4) + "," + BYTE(3));

For PSG channels:
                commands.Add("psgInst " + BYTE(4));
                commands.Add("psgNoteL " + NOTE(0x51) + "," + BYTE(4));
                commands.Add("psgNote " + NOTE(0x51));
                commands.Add("setRelease " + BYTE_HEX(0x05));
                commands.Add("vibrato " + BYTE_HEX(0x4C));
                commands.Add("shifting " + BYTE_HEX(0x10));

-------------------- EFFECTS IMPLEMENTATION ANALYSIS --------------------

Source: https://tildearrow.org/furnace/doc/v0.6.7/3-pattern/effects.html
        https://tildearrow.org/furnace/doc/v0.6.7/7-systems/ym2612.html
        https://tildearrow.org/furnace/doc/v0.6.7/7-systems/sms.html

Wiz has done interesting stuff here: https://github.com/CubeTaguchiCentral/CubeTools/blob/master/src/com/sega/md/snd/convert/furnacetocube/F2CPatternConverter.java

If an effect from Furnace documentation doesn't appear below, it means it has been deemed irrelevant, niche, too complex or unfeasible for implementation.

Effects marked with o are implemented.
Effects marked with # should be priorized and ultimately implemented.
Effects marked with + are "nice to have" but not worth getting a headache over it.
Effects marked with - are not possible unless we deviate a lot from the Furnace definition or take big risks (i.e: not worth having *BIG* headaches over it at all).

Player:
=======
o Goto order
o Goto row
o Goto order + row (when combined)
o Goto end

These can be implemented by using a Cube command:
=================================================
o Panning
# Vibrato
# Detune (all operators)
# Portamento (Set Pitch Slides)
# Set Pitch (00: -1 semitone, 80: base, FF: +1 semitone)
# Note/Frequency Shifting
# Set tick rate
# Set volume of left/right/rear left/rear right channel (< 80: OFF, >= 80: ON), yet another way of altering pan...
+ Sample offset: can be faked by introducing a new sample declaration in the sample list with desired offset, this one might be reasonable if used carefully
- Replace FM operator property effects: could be somewhat faked by adding a new instrument everytime a property changes, but this would get out of hand very quickly

NOTE: these affects are only applied when a new note plays; a warning must be issued if the user tries to use the effect elsewhere
     => Verify Furnace behavior before implementing any of them, especially when the effect is declared on cells without a new note

These could be implemented by directly altering the patterns:
=============================================================
# Note cut/delay/release = observe Furnace behavior carefully before implementing this...
     => Compliant with Furnace: rewrite patterns as needed... (annoying)
+ Arpeggio = this effect produces a rapid cycle between the current note, the note plus x semitones and the note plus y semitones
     => Compliant with Furnace: produce an artificial cycle of 3 notes: [current, current + x semitones, current + y semitones]
     => Arpeggio speed: delay between artificial notes can be changed (default 1)
     => Verify Furnace behavior and how the effect is stopped
+ Retrigger = repeats current note every x ticks, as long as the effect is present on the row
     => Compliant with Furnace: add new notes as needed
- Legato = while on, new notes instantly change the pitch of the currently playing sound instead of starting it over
     => Compliant with Furnace: (must verify) set sustain for first note, and remove note release/OFF commands until the last note of the legato
     => Technically legato cannot be implemented purely by altering the patterns as we need to enable 'sustain' on the first note
     => Verify Furnace behavior and how the effect is stopped

These effects cannot be implemented because we can't alter pitch/volume/pan/... between 2 notes:
================================================================================================
- Volume Portamento = slides the volume/pitch to the target volume/note. x is the slide speed.
- Tremolo = changes volume to be "wavy" with a sine LFO. x is the speed. y is the depth.
- Panning slide

*/