using SF2MusicCooker.Furnace;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        public const uint Mask_SFX_1 = 0b1100000000; // Read from right to left!
        public const uint Mask_SFX_2 = 0b0000111000;

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

        private static void PrintClamped(TunedMap tuned, string what)
        {
            byte[] clampedNotes = tuned.Clamped;
            if (clampedNotes.Length > 0)
            {
                string list = string.Join(", ", clampedNotes.Select(NoteBible.NameOf));
                Console.WriteLine("! The following {0} Furnace notes are too low / too high for SF2 sound engine and have been clamped instead:{1}    {2}", what, Environment.NewLine, list);
                Console.WriteLine("! To sidestep this, you can try transposing notes by changing the A-4 tuning to move them in a more favorable range");
            }
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

            // Create tuned maps
            TunedMap tuned = pitch.CreateTunedMap(file.A4Tuning);
            TunedMap tunedPsg = pitch.CreatePSGTunedMap(file.A4Tuning);

            // Check all instruments are used properly and indicate if DAC mode is enabled
            map.Check(file, _requiresDAC, out byte dac);
            DAC = dac;

            // Compute loop position
            Position loopStart = FindLoopStart(file, true);

            // Fill each channel
            for (int channel = 0; channel < file.Channels; channel++)
            {
                _channels[channel] = GenerateChannel(file, options, map, tuned, tunedPsg, channel, loopStart);
            }

            // Verify clamped notes
            PrintClamped(tuned, "YM2612");
            PrintClamped(tunedPsg, "PSG tone");

            // Update empty flag
            Empty = Array.TrueForAll(_channels, c => c == null || c == "channel_end");
        }

        private string GenerateChannel(FurnaceFile file, Options options, InstrumentMap map, TunedMap tuned, TunedMap tunedPsg, int channel, Position loopStart)
        {
            // Mask verification
            if ((_mask & (1 << channel)) == 0) return null;

            // Do not generate channels that are empty or muted
            if (!file.HasPlayNoteCommand(channel) || options.IsMuted(channel)) return "channel_end";

            // We must be able to reach the first note
            int firstNoteTicks = FindFirstNote(file, channel, options.DumpNotes);
            if (firstNoteTicks == 0) return "channel_end";

            // Identify the channel we're dealing with
            bool dac = channel == 5 && DAC == 0;
            bool psg = channel >= 6;
            bool noise = channel == 9;

            // Volume to cube helper
            Volume volume = new Volume(Volume.ParseStrategy(options.VolumeMode), file.MasterVolume * options.VolumeCoeff);
            byte VOL_F2C(byte value) => psg ? volume.PSG(value) : volume.Y2C(value);

            // Prepare state
            List<string> commands = new List<string>(file.Orders * file.Rows); // Rough estimate of the needed capacity
            HashSet<string> warnings = new HashSet<string>();
            StateSnapshot loopState = StateSnapshot.Invalid;
            bool legato = false;
            float newTimer = 0f;
            ushort psgFurnaceInstrument = 0x0000;
            ushort currentInstrument = 0xFFFF; // Will force instrument to be set on the first note
            ushort nextInstrument = 0x0000;
            byte currentVolume = 0xFF; // Will force volume to be set on the first note
            byte nextVolume = VOL_F2C((byte)(psg ? 0x0F : 0x7F)); // Max volume by default
            byte currentPan = PAN_F2C(0x00); // Center by default
            byte nextPan = currentPan;
            byte currentShifting = 0x00; // Zero by default
            byte nextShifting = currentShifting;
            byte currentSlide = 0x00; // Zero by default
            byte nextSlide = currentSlide;
            byte currentVibrato = 0xFF; // Will force the first note to set vibrato
            byte nextVibrato = 0x00;
            byte currentRelease = 0xFF; // Will force the first note to set release
            byte currentLength = 0x00; // Will force the first note or silence to set length
            byte noiseMode = 0x00;

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
                    loopState = StateSnapshot.Requested; // Will happen at the next note
                    currentRelease = 0xFF; // Will force the next note to set release
                    currentLength = 0x00; // Will force the next note or silence to set length
                }

                // Read cell content
                ReadInstrument(tick);
                ReadVolume(tick);
                ReadPan(tick, psg);
                ReadNewTimer(tick, noise || !psg);
                ReadDetune(tick, dac || noise);
                ReadPortamento(tick, dac || psg);
                ReadVibrato(tick, dac || noise);
                ReadNoiseMode(tick, !noise);
                ReadLegato(tick);

                // Apply note change
                if (cell.HasNewNote)
                {
                    GuessPSGInstrument(tick);

                    FlushPendingChanges(true, tick);

                    // Finally, write note/sample command
                    if (options.IsAllowed(currentInstrument))
                    {
                        if (dac)
                        {
                            if (map.Sample(currentInstrument, cell.Note, out byte sample))
                            {
                                WriteSample(sample, tick.NoteRelease, tick.NoteLength);
                            }
                            else
                            {
                                WriteSilence(tick.NoteLength);
                                Warning("Invalid instrument/note pair " + currentInstrument + "/" + NoteBible.NameOf(cell.Note) + " for DAC channel (unable to figure out sample to play)", tick);
                            }
                        }
                        else
                        {
                            WriteNote(cell.Note, tick.NoteRelease, tick.NoteLength);
                        }
                    }
                    else
                    {
                        WriteSilence(tick.NoteLength);
                    }
                }
                else if (cell.Note == PatternCell.NoteOff && ticks >= firstNoteTicks)
                {
                    FlushPendingChanges(false, tick);

                    // Please notice that note OFF commands are ignored if the first note hasn't been played yet
                    WriteSilence(tick.SilenceLength);
                }

                if (tick.NextPosition <= tick.Position && tick.NextPosition == loopStart)
                {
                    // Before the we cross the loop, we may have to apply some state changes...
                    if (loopState.IsValid)
                    {
                        loopState.Apply(ref nextInstrument, ref nextVolume, ref nextPan, ref nextShifting, ref nextSlide, ref nextVibrato);
                        loopState.ApplyIfSet(ref currentInstrument, ref currentVolume, ref currentPan, ref currentShifting, ref currentSlide, ref currentVibrato);
                        FlushPendingChanges(true, tick);
                    }

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

            void FlushPendingChanges(bool includeInstrument, Tick tick)
            {
                byte flags = 0;

                // Apply timer change
                if (newTimer != 0f)
                {
                    commands.Add("ymTimer " + BYTE_HEX(AsmSheetWriter.GetOptimalTimerB(newTimer)));
                    newTimer = 0f;
                }

                // Apply pan change
                if (currentPan != nextPan)
                {
                    currentPan = nextPan;
                    flags |= StateSnapshot.PAN_SET;
                    commands.Add("stereo " + BYTE_HEX(currentPan));
                }

                // Apply instrument change
                if (currentInstrument != nextInstrument && includeInstrument)
                {
                    currentInstrument = nextInstrument;

                    if (options.IsAllowed(currentInstrument))
                    {
                        if (psg)
                        {
                            // Load PSG instrument
                            flags |= StateSnapshot.INSTRUMENT_SET;
                            commands.Add("psgInst " + BYTE((byte)currentInstrument));
                        }
                        else if (!dac)
                        {
                            // Load FM instrument
                            if (map.FM(currentInstrument, out byte inst))
                            {
                                flags |= StateSnapshot.INSTRUMENT_SET;
                                commands.Add("inst " + BYTE(inst));
                            }
                            else
                            {
                                Warning("Invalid instrument " + currentInstrument + " for FM channel (is it actually an FM instrument?)", tick);
                            }
                        }
                    }
                }

                // Apply volume change
                if (currentVolume != nextVolume || (flags & StateSnapshot.INSTRUMENT_SET) != 0)
                {
                    currentVolume = nextVolume;

                    // Volume is forbidden for DAC and PSG channels
                    if (!dac && !psg)
                    {
                        flags |= StateSnapshot.VOLUME_SET;
                        commands.Add("vol " + BYTE(currentVolume));
                    }
                }

                // Apply shifting change
                if (currentShifting != nextShifting)
                {
                    currentShifting = nextShifting;
                    flags |= StateSnapshot.SHIFTING_SET;
                    commands.Add("shifting " + BYTE_HEX(currentShifting));
                }

                // Apply slide change
                if (currentSlide != nextSlide)
                {
                    currentSlide = nextSlide;
                    flags |= StateSnapshot.SLIDE_SET;

                    if (currentSlide == 0x00)
                        commands.Add("noSlide");
                    else
                        commands.Add("setSlide " + BYTE_HEX(currentSlide));
                }

                // Apply vibrato change
                if (currentVibrato != nextVibrato)
                {
                    currentVibrato = nextVibrato;

                    // Vibrato is forbidden for DAC and noise channels
                    if (!dac && !noise)
                    {
                        flags |= StateSnapshot.VIBRATO_SET;
                        commands.Add("vibrato " + BYTE_HEX(currentVibrato));
                    }
                }

                // Store state that should be reapplied just before crossing the main loop
                if (loopState.IsRequested && includeInstrument)
                {
                    loopState = new StateSnapshot(currentInstrument, currentVolume, currentPan, currentShifting, currentSlide, currentVibrato, flags);
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
                    message += Environment.NewLine + string.Format("  This is usually caused by a {0} followed by a loop that does nothing (extending it infinitely)", what);
                    message += Environment.NewLine + string.Format("  The actual {0} length will be capped in the output sheet", what);
                    Warning(message, tick);
                }
            }

            void WriteNote(byte note, int release, int length)
            {
                string value = noise ? NOISE(note) : (psg ? tunedPsg : tuned).F2CName(note);
                WriteNoteOrSample(value, release, length, psg ? "psgNote  " : "note  ", psg ? "psgNoteL " : "noteL ");
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

                if (cell.Instrument != PatternCell.InstrumentAbsent)
                {
                    if (psg)
                    {
                        psgFurnaceInstrument = cell.Instrument;
                    }
                    else
                    {
                        nextInstrument = cell.Instrument;
                    }
                }
            }

            void ReadVolume(Tick tick)
            {
                PatternCell cell = tick.ActiveChannelCell;

                if (cell.Volume != PatternCell.VolumeAbsent)
                {
                    if (dac)
                    {
                        Warning("Volume change is not allowed for channel 6 in DAC mode and will be ignored", tick);
                        return;
                    }
                    else if (!psg && !cell.HasNewNote && cell.Note != PatternCell.NoteOff)
                    {
                        Warning("Volume change will be delayed because it can only be applied when a new note plays/ends", tick);
                    }

                    nextVolume = VOL_F2C(cell.Volume);
                }
            }

            void ReadPan(Tick tick, bool disallow)
            {
                PatternCell cell = tick.ActiveChannelCell;

                if (cell.TryGetEffect(Effect.Pan, out Effect effect))
                {
                    Set(PAN_F2C(effect.Value));
                }
                else if (cell.TryGetEffect(Effect.PanTrinary, out effect))
                {
                    Set(PAN3_F2C(effect.Value));
                }

                void Set(byte pan)
                {
                    if (disallow)
                    {
                        Warning("Panning change is not allowed on this channel (must be on FM or DAC channels)", tick);
                        return;
                    }
                    else if (!cell.HasNewNote && cell.Note != PatternCell.NoteOff)
                    {
                        Warning("Panning change will be delayed because it can only be applied when a new note plays/ends", tick);
                    }

                    nextPan = pan;
                }
            }

            void ReadNewTimer(Tick tick, bool disallow)
            {
                PatternCell cell = tick.ActiveChannelCell;

                if (cell.TryGetEffect(Effect.SetTempo, out Effect effect))
                {
                    Apply(effect.Value / 2.5f);
                }

                for (int i = 0; i <= 3; i++)
                {
                    byte type = (byte)(Effect.SetTickRateBase + i);
                    if (cell.TryGetEffect(type, out effect))
                    {
                        Apply(i * 0x100 + effect.Value);
                    }
                }

                void Apply(float tickRate)
                {
                    if (disallow)
                    {
                        Warning("Play rate change effects must be put on a PSG tone channel, otherwise they will be ignored", tick);
                        return;
                    }

                    if (!cell.HasNewNote && cell.Note != PatternCell.NoteOff)
                        Warning("Play rate change will be delayed because it can only be applied when a new note plays/ends", tick);

                    newTimer = tickRate * file.RateCoeff;
                }
            }

            void ReadDetune(Tick tick, bool disallow)
            {
                // FIXME: I'm following here the CubeTools approximation of this effect even if I think it's not the accurate way to do it
                // The accurate way would be to create a shadow copy of the FM instrument with modified operators and switch to it
                // The downside is obviously that the number of FM instruments would quickly get out of hand and it's complex to set up for low payoff

                PatternCell cell = tick.ActiveChannelCell;

                if (cell.TryGetEffect(Effect.Detune, out Effect effect))
                {
                    if (disallow)
                    {
                        Warning("Detune effect is not allowed on this channel (must be on FM or PSG tone channels)", tick);
                        return;
                    }

                    if (!cell.HasNewNote && cell.Note != PatternCell.NoteOff)
                        Warning("Detune effect will be delayed because it can only be applied when a new note plays/ends", tick);

                    int detune = Math.Max(-3, Math.Min(3, effect.Value - 3));
                    if (effect.Value >= 0x07) detune = 0;

                    nextShifting = (byte)(Math.Abs(detune) << 5); // Sharper detune compared to CubeTools implementation (<< 4)
                    if (detune < 0) nextShifting |= 0x80;
                }
            }

            void ReadPortamento(Tick tick, bool disallow)
            {
                // FIXME: for better compatibility, we should also enable a 'virtual' legato on the previous note
                // This can be done by modifying the patterns

                PatternCell cell = tick.ActiveChannelCell;

                if (cell.TryGetEffect(Effect.Portamento, out Effect effect) && effect.Value > 0)
                {
                    if (disallow)
                    {
                        Warning("Portamento effect is not allowed on this channel (must be on FM channels)", tick);
                        return;
                    }
                    else if (!cell.HasNewNote)
                    {
                        Warning("Portamento effect must appear when a new note plays, otherwise it will be ignored", tick);
                        return;
                    }

                    nextSlide = (byte)Math.Min(0x7F, effect.Value * 2 - 1);
                }
                else if (cell.HasNewNote)
                {
                    nextSlide = 0x00;
                }
            }

            void ReadVibrato(Tick tick, bool disallow)
            {
                PatternCell cell = tick.ActiveChannelCell;

                if (cell.HasNewNote)
                {
                    if (tick.Vibrato.Active)
                    {
                        if (disallow)
                        {
                            Warning("Vibrato effect is not allowed on this channel (must be on FM or PSG tone channels)", tick);
                            return;
                        }

                        const byte nibble = 0x0F;
                        byte index = Math.Min(nibble, Vibrato.ResolveIndex(tick.Vibrato));
                        byte delay = Math.Min(nibble, (byte)(tick.Vibrato.Delay >> 1));

                        nextVibrato = (byte)((index << 4) | delay);
                    }
                    else
                    {
                        nextVibrato = 0;
                    }
                }
            }

            void ReadNoiseMode(Tick tick, bool disallow)
            {
                PatternCell cell = tick.ActiveChannelCell;

                if (cell.TryGetEffect(Effect.NoiseMode, out Effect effect))
                {
                    if (disallow)
                    {
                        Warning("Noise mode effect is not allowed on this channel (must be on noise channel)", tick);
                        return;
                    }

                    noiseMode = effect.Value;
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

            void GuessPSGInstrument(Tick tick)
            {
                if (psg)
                {
                    nextInstrument = PSGInstruments.Guess(file, tick, channel, psgFurnaceInstrument, nextVolume);
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

            string NOISE(byte note)
            {
                return Noise.Value(note, noiseMode).ToString();
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

        private readonly struct StateSnapshot
        {
            public readonly ushort Instrument;
            public readonly byte Volume;
            public readonly byte Pan;
            public readonly byte Shifting;
            public readonly byte Slide;
            public readonly byte Vibrato;
            public readonly byte Flags;

            public const int INSTRUMENT_SET = 1;
            public const int VOLUME_SET = 2;
            public const int PAN_SET = 4;
            public const int SHIFTING_SET = 8;
            public const int SLIDE_SET = 16;
            public const int VIBRATO_SET = 32;

            private const int REQUESTED = 64;
            private const int INVALID = 128;

            public StateSnapshot(ushort instrument, byte volume, byte pan, byte shifting, byte slide, byte vibrato, byte flags)
            {
                Instrument = instrument;
                Volume = volume;
                Pan = pan;
                Shifting = shifting;
                Slide = slide;
                Vibrato = vibrato;
                Flags = flags;
            }

            public void Apply(ref ushort instrument, ref byte volume, ref byte pan, ref byte shifting, ref byte slide, ref byte vibrato)
            {
                instrument = Instrument;
                volume = Volume;
                pan = Pan;
                shifting = Shifting;
                slide = Slide;
                vibrato = Vibrato;
            }

            public void ApplyIfSet(ref ushort instrument, ref byte volume, ref byte pan, ref byte shifting, ref byte slide, ref byte vibrato)
            {
                if ((Flags & INSTRUMENT_SET) != 0) instrument = Instrument;
                if ((Flags & VOLUME_SET) != 0) volume = Volume;
                if ((Flags & PAN_SET) != 0) pan = Pan;
                if ((Flags & SHIFTING_SET) != 0) shifting = Shifting;
                if ((Flags & SLIDE_SET) != 0) slide = Slide;
                if ((Flags & VIBRATO_SET) != 0) vibrato = Vibrato;
            }

            public bool IsValid => (Flags & INVALID) == 0;

            public bool IsRequested => (Flags & REQUESTED) != 0;

            public static StateSnapshot Invalid => new StateSnapshot(0, 0, 0, 0, 0, 0, INVALID);

            public static StateSnapshot Requested => new StateSnapshot(0, 0, 0, 0, 0, 0, INVALID | REQUESTED);
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
                commands.Add("sustain");
                commands.Add("setRelease " + BYTE_HEX(0x05));

For FM channels:
                commands.Add("inst " + BYTE(0));
                commands.Add("vol " + BYTE_HEX(0x0C));
                commands.Add("vibrato " + BYTE_HEX(0x2C));
                commands.Add("setSlide " + BYTE_HEX(0x20));
                commands.Add("noSlide");
                commands.Add("shifting " + BYTE_HEX(0x20));
                commands.Add("note " + NOTE(0x51));
                commands.Add("noteL " + NOTE(0x51) + "," + BYTE(24));

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
                commands.Add("ymTimer " + BYTE_HEX(0xC8));

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
Effects marked with ! are deemed impossible to implement.

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
o Set tick rate / tempo
o Detune for all operators (approximation based on WizTools implementation)
o Portamento (Set Pitch Slides)
o Vibrato (+ Set Vibrato Shape)
    NOTE: these affects are only applied when a new note plays; a warning must be issued if the user tries to use the effect elsewhere

These could be implemented by directly altering the patterns:
=============================================================
+ Note cut/delay/release = observe Furnace behavior carefully before implementing this...
    => Compliant with Furnace: rewrite patterns as needed... (annoying)
+ Arpeggio = this effect produces a rapid cycle between the current note, the note plus x semitones and the note plus y semitones
    => Compliant with Furnace: produce an artificial cycle of 3 notes: [current, current + x semitones, current + y semitones]
    => Arpeggio speed: delay between artificial notes can be changed (default 1) [modify PatternCell.Multiply method!]
    => Verify Furnace behavior and how the effect is stopped
+ Retrigger = repeats current note every x ticks [modify PatternCell.Multiply method!], as long as the effect is present on the row
    => Compliant with Furnace: add new notes as needed
+ Sample offset: can be faked by introducing a new sample declaration in the sample list with desired offset, this one might be reasonable if used carefully
- Replace FM operator property effects: could be somewhat faked by adding a new instrument everytime a property changes, but this would get out of hand very quickly
- Set panning of left/right channel: tedious because we need to keep track of previous panning commands, not just Cube current panning value

These effects cannot be implemented (unless we add artificial notes) because we can't alter pitch/volume/pan/... between 2 notes:
=================================================================================================================================
- Volume Portamento = slides the volume/pitch to the target volume/note. x is the slide speed.
- Tremolo = changes volume to be "wavy" with a sine LFO. x is the speed. y is the depth.
! Panning slide (panning is either ON or OFF so this is completely out of reach, outside of manual binary sliding of course)

These effects cannot be implemented because of limitations of Cube sound driver:
================================================================================
- Set pitch (00: -1 note, 80: base, FF: +1 note): we don't have enough leeway to adjust frequencies between 2 notes (can only do +/- 14 hz)
- Single tick pitch up/down: same as above, we cannot shift further than 14 hz and this would consume a ton of space
- Note shifting: not really useful, it doesn't allow us to reach extra octaves; we are still limited to the palette of YM frequencies
! Setup LFO: sound driver doesn't support LFO register which is gonna stay disabled, this means no AMS/PMS either (amplitude, frequency modulation)

*/