# Project notes for Claude

See `PLAN.md` for the goal, design, and task list.

## Game install

- Beat Saber 1.40.8 is installed at `D:\Software\BSManager\BSInstances\1.40.8`.
- **Do not write to that folder without asking first.** This includes builds that copy the DLL into `Plugins`.
- Reading from it is fine: decompiling the game assemblies, and checking which other mods are installed (in `Plugins` and `Libs`) that may need extra work for compatibility.

## Workflow

- When an implementation step is complete, commit and push.
