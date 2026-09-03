PREAMBLE
--------

We assume the following:
- You have basic knowledge of how SF2DISASM folders are structured.
- You are working in a branch in which 'feature/expand_musics' feature branch has been merged.
- You have basic knowledge of the Furnace tracker software.
- You have composed music tracks in Furnace and saved them as .fur files (or you have received such files from someone else who composed them).
- You are interested in importing these music tracks to replace existing music tracks or add new music tracks into your Shining Force 2 ROM hack. ;-)



HOW TO USE
----------

- Put a bunch of .fur files into the "Input" folder with one of the following name patterns:
	+musicXX-My new song title.fur		[ADD = XX is a music number NOT USED by vanilla SF2 songs, between 49-64 when using extra music banks]
	@musicYY-My replacement song.fur	[REPLACE = YY is a music number USED by a vanilla SF2 song, please refer to "disasm/enum/musics.asm" for the available list]
	+musicXX@YY-My moved song.fur		[MOVE-REPLACE = XX is a music number NOT USED by vanilla SF2 songs and YY is a music number USED by a vanilla SF2 song]
- As shown above, you can ADD NEW MUSICS or REPLACE EXISTING MUSICS or MOVE-REPLACE EXISTING MUSICS depending on how you name your .fur file.
- You can combine adding new musics, replacing existing musics and move-replacing existing musics together.
- To test quickly in emulator if a song sounds good in the game, you can replace music 9 (played during the game intro) or 33 (played in test menu).
	@music9-Quick test.fur		or		@music33-Quick test.fur
- Adding new musics is only useful if you modify map data or game code to use them (i.e: this use case is for the more ambitious projects).
- Replacing existing musics is the more common use case and is intended for hacks with smaller scopes. There is a small caveat with 2 problematic music pairs (explained below).
- The Furnace files MUST use EXACTLY and ONLY the Genesis chips (YM2612 + SMS PSG), Furnace files that are not compliant will be rejected by the tool.
- Run SF2MusicCooker.exe with first argument = path to your SF2DISASM folder and pray to the gods your Furnace files are simple enough to be handled properly :-)
	TIP: You can use the __Run.bat script to make this more convenient (please edit it the first time to put the correct path to SF2DISASM folder)
- If the tool fails to handle a .fur file, an error message will be displayed that hopefully should explain what went wrong so you can tweak your .fur file.
- If everything goes well, the tool will write files to the appropriate locations in your SF2DISASM folder.
- You can also ask the tool to write files to 'Output' folder instead if you want to manually review the generated files.
- After generating the ROM, it is a good idea to go to the sound test and verify everything works properly.



HOW TO USE (SFX)
----------------

- You can also replace SFXs (sound effects) by naming slightly differently your .fur files in the "Input" folder:
	+sfxZZ-My new sound effect.fur			[ADD = ZZ is a SFX number NOT USED by vanilla SF2 SFXs, between 121-126] (yes it's quite tight)
	@sfxWW-My replacement sound effect.fur	[REPLACE = WW is a SFX number USED by a vanilla SF2 SFXs, please refer to "disasm/enum/sfxs.asm" for the available list]
- For SFXs, you cannot MOVE-REPLACE EXISTING SFXS. You can only ADD NEW SFXS or REPLACE EXISTING SFXS.
- Everything that applies above to musics also apply to SFXs.
- Except for this: in vanilla game SFX data, some SFXs depend on other SFXs (due to shared pointers).
- If you replace a SFX that is a dependency to other SFXs, the tool will error unless you replace ALL involved SFXs, all at once.



GOLDEN RULES (this section is especially relevant to music composers)
------------

1. Please create custom Sega Genesis songs in Furnace in the most basic way: no macros, no extreme octave notes, no exotic effects (see "LIMITATIONS" below for more details).
2. Your song should rely mostly on the standard 5 FM channels + the 6th channel.
3. You can use samples in channel 6 (but you won't be able to it as FM channel for the *full* song). Samples should be small and you can't have too many of them.
4. Important instruments should use FM channels 0, 1, 2 because other channels can be borrowed by sound effects during SF2 gameplay.
5. Space in the ROM is limited and you should avoid extremely long songs with a ludicrous number of FM instruments / samples ( but you can still try ;-) ).



ROM SPACE
---------

The .fur files you submit to the tool will ultimately have to fit into the Music Banks of the ROM, otherwise the assembly will fail when you build the ROM.
Each Music Bank can hold 32768 bytes and SF2 Music Cooker will add a comment into each generated music .asm file to give an estimation of how many bytes the song will take.

The vanilla game comes with Music Bank 0 (musics 1 to 32) and Music Bank 1 (musics 33 to 64). Sadly, these two Music Banks are *almost* full.
For this reason, REPLACE EXISTING MUSICS feature can be pretty limited if you try to import songs that take more space than their vanilla music counterpart.

Thanks to the people at SF2Central that worked on expanding game ROM patches, it is now possible to add extra Musics Banks inside the expanded ROM space space.
If you enable the 'EXPANDED_MUSIC_BANKS' patch, you will have access to 2 extra, fully available Music Banks to host all your custom music needs.
These are called Music Bank Ext 0 (musics 49 to 56) and Music Bank Ext 1 (musics 57 to 64).
As you can see, the Music Bank 1 range was cut down compared to the vanilla game. Its range is now 33 to 48.

The MOVE-REPLACE EXISTING MUSIC feature allows you to replace a vanilla music by a custom music, while putting the new music into Music Bank Ext 0 and Music Bank Ext 1.

For SFXs, it appears the SFX Bank from the vanilla game still has some space available, so space should (hopefully) not be a concern here.
There is no Extra Bank feature for SFX Bank, you have to fit everything into a single bank.

For musics and SFXs that use samples, the vanilla game comes with 2 PCM banks (PCM Bank 0 and PCM Bank 1) that are pretty much full.
[NOT YET AVAILABLE] Two Extra PCM banks (PCM Bank Ext 0 and PCM Bank Ext 1) have been implemented with 'EXPANDED_PCM_BANKS' patch to provide some breathing room.



OPTIONS
-------

SF2 Music Cooker can be run with the following option switches to alter its behavior: (must appear after the 1st argument)

--includeoriginalnames		or		-ion	Include original names of replaced musics (in the sound test)
--nukemusic					or		-nm		Replace musics from vanilla game by empty musics (can be useful as a temporary bank size issue workaround)
--nukesfx					or		-ns		Replace SFXs from vanilla game by empty SFXs (can be useful as a temporary bank size issue workaround)
--nukeall					or		-na		Replace everything from vanilla game by emptiness (equivalent to --nukemusic --nukesfx)
--nopause					or		-np		Do not ask to press a key to quit the program
--nopostbuild				or		-npb	Do not run POSTBUILD.bat, even if it's provided
--autoyes					or		-ay		Automatically confirm to write files to SF2DISASM folder
--autono					or		-an		Automatically confirm to write files to Output folder (has priority over --autoyes)
--input=X					or		-i=X	Read files in "X" folder instead of "Input" folder (only useful for developers)
--onlyN						or		-oN		(N = 1..126) Only process the .fur file for the given music/SFX number and ignore the rest (only useful for developers)

The following options can be specified globally or per-music/per-SFX:

--muteN						or		-mN		(N = 1..10) If present disable output of channel N (you can stack multiple --muteN options)
--isolateN					or		-iN		(N = 1..10) If present disable output of all channels except channel N (you can stack multiple --isolateN options)
--muteinstrument=x			or		-mi=x	Mute Furnace instrument x in the output (can be stacked multiple times with different values)
--mutesamples				or		-ms		Process Furnace file as if it had empty samples (can be useful to check if issues are caused by samples)
--preserverate				or		-pr		Disable tricks to support properly musics below 13 hz and fix SFXs play speed (see caveat below for explanations)
--removerelease				or		-rr		Remove all note release commands (===) in all channels
--removeoff					or		-ro		Remove all note off commands (OFF) in all channels
--nooptimize				or		-no		Do not reduce the size of the music sheet with 'countedLoopStart/End' blocks (implicitly set if --dumpnotes is used)
--dumpnotes					or		-dn		Write Furnace tracker commands alongside produced ASM commands in the music sheet (only useful for developers or curious people)
--dumpuncompressed			or		-du		Write a copy of decompressed input Furnace file into "Uncompressed" folder (only useful for developers)
--sampleratecoeff=x			or		-src=x	Scale the sample rate of all samples by x (default is x=1.0, and sound engine has a limit when playing very high pitched samples)
--volume=x					or		-v=x	Multiply the master volume by x (can be useful to get rid of saturated output without touching the original .fur file)
--volume:linear				or		-v:l	Use linear volume (technically incorrect but gives interesting results depending on the music)
--volume:nearest			or		-v:n	Use nearest instead of truncating when mapping YM volume to SF2 sound driver volume (gives subtly different results)

To apply options per-music/per-SFX, provide an "options.txt" file in the "Input" folder.
Example file (dump notes of music 33 and mute samples of music 41):
	33 = --dumpnotes
	41 = --mutesamples



LIMITATIONS
-----------

SF2 Music Cooker currently supports:
- FM channels 1 to 5
- Channel 6 in FM mode
- Channel 6 in DAC mode (samples) 
- Notes between C-0 and B-9 on Furnace side (120 notes), unsupported notes are suppressed
- Furnace notes are mapped to the SF2 engine notes in a best effort fashion (84 notes), in practice this means extreme notes are clamped between C-1 and B-7
- Arbitrary tempo for musics (see below for special caveats about the tick rate)
- New FM instruments, these will get added to the vanilla SF2 instruments
- New samples, these will get added to the vanilla SF2 samples (almost impossible to do without using expanded PCM banks)
- Effects supported by the SF2 sound driver, such as volume, panning, vibrato
- Song loop/end marker effects, as well as "jump to next pattern" effect
- Song size reduction by detecting "repeated command patterns" and replacing them with counted loops
- Reasonably long songs that use many FM instruments and notes (as long as it can fit in the assembled ROM)
- New songs will appear in the Sound Test; the Sound Test is also improved with better SFX names and circular navigation

SF2 Music Cooker doesn't currently support but will in the future (because I want to do it):
- PSG channels (3 square + 1 noise) with enveloppe macros
- Additional Furnace effects and better mapping from Furnace to SF2 sound engine

SF2 Music Cooker doesn't plan to support (unless there is an *overwhelming* demand for the feature):
- Additional song size reduction by detecting voltas (repeats with different endings)
- Furnace features such as "macros" (for FM instruments or samples), "groove", "speed 2" and other gimmicks/effects/compatibility flags (sorry)
- Other file formats, such as VGM format, you will have to adapt them yourself into .fur files before using this tool (the 'vgm2fur' Python tool is promising here)
- Shining Force 1 (maybe most of the puzzle pieces are already solved and it wouldn't require *that* much effort, but I didn't look at SF1 DISASM and music engine at all)

Special caveat about tick rates:
- Musics can theorically have a tick rate between 13 hz and 3329 hz (this is the possible frequency range for the YM2612 timer)
- Most SF2 vanilla musics play at 60 hz
- SFXs *cannot* specify a tick rate, and their effective speed is based on the music tick rate
- SFXs seem to play at the correct speed if music is between 30 and 60 hz (lower makes SFXs play slower, higher makes SFXs play faster)
- Furnace files for SFXs should have a play rate between 58 and 62 hz (files slower than 30 hz will be multiplied to land in this range if possible)
- This tool will try to artificially multiply frequency and pattern rows of musics to reach a tick rate >= 13 hz that plays nice with the SFXs
- TL;DR / To summarize: Furnace files should play at 60 hz for SFXs, and somewhere between 30~60 hz for musics

Special caveat for replacing music pairs (3, 4) and (13, 14):
- Vanilla SF2 musics "Mitula Shrine" and "Elven Town" (13, 14) are closely related and combined together in the SF2DISASM music assembly files
- The same applies for "Promoted Attack" and "Promoted Attack Loop" (3, 4)
- This makes it very inconvenient to replace each music separately without affecting the other
- You CAN replace these musics, but if you don't provide a replacement for the other music in the pair, the music you provided will also replace the other music in the pair
- You CANNOT move-replace these musics