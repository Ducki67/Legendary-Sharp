<img src="assets/icon.png" alt="Legendary C# icon" width="128" align="right">

# Legendary C#

Fortnite build downloader for the OG community. A C# rewrite of [legendary](https://github.com/legendary-gl/legendary),
built for pulling old builds from Epic's CDN.


**No Epic account needed.** Manifests come from public archives, chunks come from Epic's public CDN.



- Single 8 MB native exe with no dependencies, or a small installer that adds it to your terminal
- Reads both manifest formats, binary and the pre-2021 JSON ones
- Arrow key menu with mouse scrolling and click to pick, no commands to learn
- Install tag picker with real sizes, so a full build is one keypress
- Tells you up front how much of an old build Epic still hosts
- Adds UEFN to any build from 24.20, matched by changelist
- Resumes after a crash or pause, even in the middle of a file, and verifies every chunk and file against the manifest
- Cancel removes only the files that download wrote
- Add or remove install tags on a build you already have
- Picks up builds you got elsewhere
- Queue as many downloads as you like, they run in the background and ease off when your PC is busy


## Install

Grab `LegendarySharp-vX.Y.Z.msi` from the [releases](https://github.com/Ducki67/Legendary-Sharp/releases)
and run it. No admin rights needed. It installs for your account only and adds:

- the `legendary#`, `L#` and `Lsharp` commands to cmd and PowerShell
- a **Legendary C#** entry in the Windows Terminal dropdown
- Start menu and desktop shortcuts

Running a newer msi updates it in place. Prefer no install? The plain `LegendarySharp-vX.Y.Z.exe` runs
from anywhere.

## Use

Run `L#` in a terminal or double click `LegendarySharp.exe`. Everything is in the menu:

| | |
| --- | --- |
| **Download a build** | Pick a season or jump straight to builds confirmed to download in full, choose how much of it you want, and go |
| **My builds** | Verify, repair, resume an interrupted download, change install tags, uninstall, add UEFN and add builds found in your build folders |
| **Downloads** | Everything queued, running or finished, with pause, resume, reorder and cancel |
| **Browse releases** | Every known build, type to filter, open one to check availability, read its manifest or download it |
| **Settings** | Install folder, build folders, workers, memory budget, default preset, mouse and scrolling |

Downloads go to `Documents\FortniteBuilds` unless you change the install root. The suggested folder is
highlighted, so typing, pasting or dragging a folder in replaces it.

Run it from a terminal inside a builds folder, any folder with "builds" in its path like
`D:\FortniteBuilds\ch3`, and it goes straight to picking a build and downloads it into a folder named
after the build right there.

## Install tags

Tag naming changed three times. All three are understood, so presets and sizes are right on any build.

| Era | Builds | Tags |
| --- | --- | --- |
| Chunk | 13.40 to 18.30 | `chunk0`, `chunk10optional` |
| Named | 21.10 to 33.x | `core`, `br`, `stw`, `loc_de` |
| Gameplay | 34.10 and newer | `Startup`, `FortniteBR`, `GFP_JunoRoot` |

Presets run from **Full build** down to **Minimal**, with **Battle Royale** and **Save the World** in
between. You can also tick tags individually, with a running size total, or load a selective download
json if you have one for that build era.

## Old builds

Epic prunes chunks, so most old builds will not download in full no matter what tags you use. Every
Windows build up to 23.50 was checked chunk by chunk against the CDN, and it is all or nothing: a build
either downloads in full with any preset, or it is missing core files even on **Minimal**.

The app knows the result for each build. Working ones get a tick in the menu, broken ones say how many
files they will be missing. The full list is in [docs/BUILDS.md](docs/BUILDS.md).

Open any build under **Browse releases** and choose **Check availability** to sample the CDN before
committing. If part of it is gone you are offered the chance to take whatever survives, and told
afterwards which files could not be built.

## UEFN

Builds from 24.20 have a matching UEFN release, paired by changelist, installed into the same folder.
You are offered it after a download, or add it later from **My builds**. Turn the prompt off in
**Settings**.

## Downloads

Starting a download adds it to **Downloads** and hands you back the menu, with a live status line on top.
Queue as many as you like, they run one at a time. Pause, resume, reorder or cancel them from the
Downloads screen, and close the app whenever, the queue is offered back next time you open it.

A pause survives closing the app. If you close it mid-download, the download is offered back on the next
start, and you can choose to delete what it downloaded instead. Cancel deletes only the files that
download wrote, nothing else in the folder.

While one runs it eases off when Fortnite is open, when other programs max out the CPU or when memory is
nearly full, and speeds back up after. Turn that off in **Settings**. Downloads from other windows take
turns with yours instead of fighting over bandwidth.

## Something went wrong

Errors are saved to `%LOCALAPPDATA%\LegendarySharp\log.txt`. Attach it when you open an issue.

## Build it

Needs the .NET 10 SDK. A Release build publishes the native single file exe automatically and drops it
in `publish/`.

```bash
dotnet build "Legendary Sharp/Legendary Sharp.csproj" -c Release
```

The native step needs the Visual Studio C++ build tools. Without them the build still succeeds, it
just warns and leaves you the managed build. Skip it deliberately with `-p:PublishAfterBuild=false`.

Debug builds never publish.

## Contributing

Most useful thing to add is confirmed builds. If you finish one that is not ticked, add its changelist
to [`Fortnite/VerifiedBuilds.cs`](Legendary%20Sharp/Fortnite/VerifiedBuilds.cs).

The code has no comments by design. If something needs one to be understood, it needs renaming.

## Credits

- [derrod](https://github.com/derrod) for [legendary](https://github.com/legendary-gl/legendary), where the manifest
  and chunk formats were worked out. Independent implementation, no code shared.
- [polynite/fn-releases](https://github.com/polynite/fn-releases),
  [VastBlast/FortniteManifestArchive](https://github.com/VastBlast/FortniteManifestArchive) and
  [Mast3rGamers/UEFN-releases](https://github.com/Mast3rGamers/UEFN-releases) for the manifest archives.
- Helix, for which builds still download in full and which tags each era needs.

## License

MIT. Free to use, modify and redistribute, including commercially. The only condition is that the
copyright notice and licence stay with it, so credit comes back to this repo.

legendary is GPL-3.0, but none of its code is here. This is an independent implementation of Epic's
file formats, which are not copyrightable. Only the name of the project is re-used.
