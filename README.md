# SF2MusicCooker
Tool to assemble new musics for Shining Force 2 sound engine from Furnace (.fur) files

## Purpose of this project
The purpose of this project is to create a convenient tool that allows the Shining Force 2 ROM hack community to easily add and replace musics in the game.
The tool takes a bunch of Furnace files (.fur) and outputs assembly files (.asm) to replace files from the original SF2DISASM disassembly.

## Challenges
Hacking new musics into Shining Force 2 sound engine comes with a bunch of challenges and traps, some of which I'm still actively working on.
- Limited ROM space (solved by the commmunity through ROM expansion)
- Limited number of FM instruments
- Limited bank space that the Z80 CPU needs to access for music sheets, samples and FM instruments
- Complexity of FM synthesis
- Complexity of mapping Furnace music patterns to Shining Force 2 sound engine commands
- And a lot of other things I forgot :)

## How to build the project
This tool is programmed using C# language and runs on .NET 4.8.1 framework.
Simply open the .sln file in Visual Studio 2022 or 2026 (Community edition will do).