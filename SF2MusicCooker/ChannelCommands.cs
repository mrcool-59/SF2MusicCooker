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
        public void Generate(FurnaceFile file, Options options, InstrumentMap map, PitchTable pitch)
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
                _channels[channel] = GenerateChannel(file, options, map, pitch, channel, loopStart);
            }

            // Update empty flag
            Empty = Array.TrueForAll(_channels, c => c == null || c == "channel_end");
        }

        private string GenerateChannel(FurnaceFile file, Options options, InstrumentMap map, PitchTable pitch, int channel, Position loopStart)
        {
            // Mask verification
            if ((_mask & (1 << channel)) == 0) return null;

            // Do not generate channels that are empty or muted
            if (!file.HasPlayNoteCommand(channel) || options.IsMuted(channel)) return "channel_end";

            // We must be able to reach the first note
            int firstNoteTicks = FindFirstNote(file, channel, options.DumpNotes);
            if (firstNoteTicks == 0) return "channel_end";

            // Volume to cube helper
            Volume volume = Volume.FromStrategy(null); // TODO
            byte VOL_F2C(byte ymVolume) => volume.Y2C(ymVolume);

            // Prepare state
            List<string> commands = new List<string>(file.Orders * file.Rows); // Rough estimate of the needed capacity
            HashSet<string> warnings = new HashSet<string>();
            bool legato = false;
            ushort currentInstrument = 0x0000;
            byte currentVolume = VOL_F2C(0x7F); // Max volume by default
            byte currentPan = PAN_F2C(0x00);
            byte currentRelease;
            byte currentLength;
            bool instrumentChanged;
            bool volumeChanged;
            bool panChanged;

            // Initial state
            ForceRefresh();

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

                // For troubleshooting
                if (options.DumpNotes) commands.Add("; " + tick.Dump());

                // Verify that note/silence length we got is not absurd
                VerifyExtremeLength(tick, maxPredictLength);

                // Mark beginning of loop
                if (tick.Position == loopStart)
                {
                    commands.Add("mainLoopStart");
                    ForceRefresh(); // Otherwise we would have wrong note lengths etc. after crossing the loop!
                    // FIXME: we can save a bunch of bytes here if we only refresh what is necessary, but that would make the code more convoluted.
                }

                // Is it FM or PSG channel?
                if (channel < 6)
                {
                    // Read cell content
                    ReadInstrument(tick);
                    ReadVolume(tick, true);
                    ReadPan(tick, false);
                    ReadLegato(tick);

                    // Apply note change
                    if (cell.HasNewNote)
                    {
                        FlushPendingChanges();

                        // Finally, write note/sample command
                        if (map.Sample(currentInstrument, cell.Note, out byte sample))
                        {
                            WriteSample(sample, tick.NoteRelease, tick.NoteLength);
                        }
                        else
                        {
                            WriteNote(cell.Note, tick.NoteRelease, tick.NoteLength);
                        }
                    }
                    else if (cell.Note == PatternCell.NoteOff && ticks >= firstNoteTicks)
                    {
                        FlushPendingChanges();

                        // Please notice that note OFF commands are ignored if the first note hasn't been played yet
                        WriteSilence(tick.SilenceLength);
                    }

                    // TODO: remaining FM effects to implement/review (see below)
                }
                else
                {
                    // Read cell content with PSG logic
                    ReadInstrument(tick);
                    ReadVolume(tick, false); // We also use volume outside of new PSG note as a hint to select the correct PSG instrument, so don't raise warning here
                    ReadPan(tick, true); // Just to verify we don't try to pan PSG instruments

                    // TODO: PSG channel grammar
                }

                if (tick.NextPosition <= tick.Position && tick.NextPosition == loopStart)
                {
                    // Mark ending of loop and exit
                    commands.Add("mainLoopEnd");
                    break;
                }
                else if (tick.NextPosition >= file.End)
                {
                    // We reached the end (can only occur if there's no loop)
                    commands.Add("channel_end");
                    break; // Technically overkill since current tick would have been the last tick of enumeration
                }
            }

            // List to array
            string[] finalCommands = commands.ToArray();

            // Apply optimization (only if dumping is disabled)
            if (!options.NoOptimize && !options.DumpNotes) finalCommands = optimizer.Optimize(finalCommands);

            // Get the assembly
            string asm = string.Join(_separator, finalCommands);

            // Remove empty loops (can happen if we play 1 note that we infinitely sustain in a loop that does nothing)
            asm = asm.Replace("mainLoopStart" + _separator + "mainLoopEnd", "channel_end");

            // Channel done!
            return asm;

            // ------------------------------ Helpers -------------------------------

            void ForceRefresh()
            {
                currentRelease = 0xFF; // Will force the first note to set release
                currentLength = 0x00; // Will force the first note or silence to set length
                instrumentChanged = true;
                volumeChanged = true;
                panChanged = true;
            }

            void FlushPendingChanges()
            {
                // Apply pan change
                if (panChanged)
                {
                    panChanged = false;
                    commands.Add("stereo " + BYTE_HEX(currentPan));
                }

                // Apply instrument change
                if (instrumentChanged)
                {
                    instrumentChanged = false;
                    if (map.FM(currentInstrument, out byte inst))
                    {
                        commands.Add("inst " + BYTE(inst));
                    }
                }

                // Apply volume change
                if (volumeChanged)
                {
                    volumeChanged = false;
                    commands.Add("vol " + BYTE(currentVolume));
                }
            }

            void Warning(string what, Tick tick)
            {
                if (warnings.Add(what))
                {
                    string message = string.Format("! Channel {0} @ {1} -> {2}", GetChannelName(channel), tick.Position, what);
                    Console.WriteLine(message);
                }
            }

            void VerifyExtremeLength(Tick tick, int lengthThreshold)
            {
                string what = null;
                int length = 0;
                if (tick.SilenceLength >= lengthThreshold)
                {
                    what = "Silence";
                    length = tick.SilenceLength;
                }
                if (tick.NoteLength >= lengthThreshold)
                {
                    what = "Note";
                    length = tick.NoteLength;
                }
                if (what != null)
                {
                    string message = string.Format("{0} has hit the maximum allowed length ({1} ticks)", what, length);
                    what = what.ToLower();
                    message += Environment.NewLine + string.Format("  This is usually caused by a {0} followed by a loop that does nothing (extending it infinitely).", what);
                    message += Environment.NewLine + string.Format("  The actual {0} length will be capped in the output sheet.", what);
                    Warning(message, tick);
                }
            }

            void WriteNote(byte note, int release, int length)
            {
                WriteNoteOrSample(NOTE(note), release, length, "note  ", "noteL ");
            }

            void WriteSample(byte sample, int release, int length)
            {
                WriteNoteOrSample(BYTE(sample), release, length, "sample  ", "sampleL ");
            }

            void WriteNoteOrSample(string value, int release, int length, string command, string commandL)
            {
                int cappedLength = Math.Min(length, release + 0x7F); // After releasing a command, we can't have it play for more than 0x7F ticks
                int extraSilence = length - cappedLength;

                length = cappedLength;

                while (length > 0)
                {
                    if (legato && length == release)
                        WriteSetReleaseOrSustain(-1); // Legato is enabled and this note doesn't have a key release
                    else if (length >= 0x100)
                        WriteSetReleaseOrSustain(-1); // This command is sustained because it goes above the max length of a single command (i.e: another one is required)
                    else
                        WriteSetReleaseOrSustain(length - release); // This command will end, we can also set when it should be released

                    byte newLength = (byte)Math.Min(0xFF, length);
                    if (currentLength != newLength)
                    {
                        currentLength = newLength;
                        commands.Add(commandL + value + "," + BYTE(currentLength));
                    }
                    else
                    {
                        commands.Add(command + value);
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

            void ReadInstrument(Tick tick)
            {
                PatternCell cell = tick.ActiveChannelCell;

                // No warning needed here as Furnace doesn't apply instrument change immediately

                if (cell.Instrument != currentInstrument && cell.Instrument != PatternCell.InstrumentAbsent)
                {
                    currentInstrument = cell.Instrument;
                    instrumentChanged = true;
                }
            }

            void ReadVolume(Tick tick, bool disallowOutsideOfNewNote)
            {
                PatternCell cell = tick.ActiveChannelCell;

                if (cell.Volume != PatternCell.VolumeAbsent && currentVolume != VOL_F2C(cell.Volume))
                {
                    if (disallowOutsideOfNewNote && !cell.HasNewNote)
                        Warning("Volume change will be delayed because it can only be applied when a new note plays.", tick);

                    currentVolume = VOL_F2C(cell.Volume);
                    volumeChanged = true;
                }
            }

            void ReadPan(Tick tick, bool disallowPanning)
            {
                PatternCell cell = tick.ActiveChannelCell;

                if (cell.TryGetEffect(Effect.Pan, out Effect effect))
                {
                    Try(PAN_F2C(effect.Value));
                }
                else if (cell.TryGetEffect(Effect.PanTrinary, out effect))
                {
                    Try(PAN3_F2C(effect.Value));
                }

                void Try(byte newPan)
                {
                    if (disallowPanning)
                        Warning("Panning change is not allowed on this channel.", tick);
                    else if (!cell.HasNewNote)
                        Warning("Panning change will be delayed because it can only be applied when a new note plays.", tick);

                    if (currentPan != newPan)
                    {
                        currentPan = newPan;
                        panChanged = true;
                    }
                }
            }

            void ReadLegato(Tick tick)
            {
                PatternCell cell = tick.ActiveChannelCell;

                if (cell.TryGetEffect(Effect.Legato, out Effect effect))
                {
                    legato = effect.Value != 0x00;
                }
            }

            byte PAN_F2C(byte pan)
            {
                bool disableLeft = (pan & 0x0F) != 0;
                bool disableRight = (pan & 0xF0) != 0;
                if (disableLeft && disableRight) { disableLeft = disableRight = false; }
                return (byte)((disableLeft ? 0 : (1 << 7)) | (disableRight ? 0 : (1 << 6)));
            }

            byte PAN3_F2C(byte trinaryPan)
            {
                if (trinaryPan == 0x00) return 1 << 7; // LEFT
                else if (trinaryPan == 0xFF) return 1 << 6; // RIGHT
                else return (1 << 6) | (1 << 7); // CENTER (including invalid values)
            }

            string NOTE(byte value)
            {
                const int OFFSET = -24;
                int note = pitch.MapF2YNote(value, file.A4Tuning, out int difference);
                note -= OFFSET;
                // TODO: 'shifting' to reach normally unsupported octaves?
                return pitch.GetYMNoteName(note);
            }
        }

        private Position FindLoopStart(FurnaceFile file, bool print)
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

        private int FindFirstNote(FurnaceFile file, int channel, bool print)
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
            if (ticks > 0)
            {
                if (firstNoteTicks <= 0)
                    Console.WriteLine("! Channel {0} first note never happens", GetChannelName(channel));
                else if (print)
                    Console.WriteLine("> Channel {0} first note tick: {1}", GetChannelName(channel), ticks);
            }
            return firstNoteTicks;
        }

        private string GetChannelName(int channel)
        {
            if (channel == 5 && DAC == 0)
                return "FM 6 (DAC)";
            else if (channel <= 5)
                return "FM " + (channel + 1);
            else if (channel <= 8)
                return "Square " + (channel - 5);
            else
                return "Noise";
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
o Legato
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

These effects cannot be implemented because we can't alter pitch/volume/pan/... between 2 notes:
================================================================================================
- Volume Portamento = slides the volume/pitch to the target volume/note. x is the slide speed.
- Tremolo = changes volume to be "wavy" with a sine LFO. x is the speed. y is the depth.
- Panning slide

*/