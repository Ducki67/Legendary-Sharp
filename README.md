

# Legendary C#

Fortnite build downloader for the OG community. A C# rewrite of [legendary](https://github.com/legendary-gl/legendary),
built for pulling old builds from Epic's CDN.


**No Epic account needed.** Manifests come from public archives, chunks come from Epic's public CDN.



- Single 7.5 MB native exe, no install, no dependencies
- Reads both manifest formats, binary and the pre-2021 JSON ones
- Arrow key menu with mouse scrolling and click to pick, no commands to learn
- Install tag picker with real sizes, so a full build is one keypress
- Tells you up front how much of an old build Epic still hosts
- Adds UEFN to any build from 24.20, matched by changelist
- Resumes after a crash, verifies every chunk and file against the manifest
- Run as many windows as you like, downloads queue up and take turns


## Use

Double click `LegendarySharp.exe`. Everything is in the menu:

| | |
| --- | --- |
| **Download a build** | Pick a season or jump straight to builds confirmed to download in full, choose how much of it you want, and go |
| **My builds** | Verify, repair, resume an interrupted download, or add UEFN |
| **Browse releases** | Every known build, type to filter, open one to check availability, read its manifest or download it |
| **Settings** | Install folder, workers, memory budget, default preset, mouse and scrolling |

Downloads go to `Documents\FortniteBuilds` unless you change the install root.

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

Epic prunes chunks, so most builds below 13.40 will not download in full no matter what tags you use.
Builds confirmed to complete are marked with a tick in the menu.

Open any build under **Browse releases** and choose **Check availability** to sample the CDN before
committing. If part of it is gone you are offered the chance to take whatever survives, and told
afterwards which files could not be built.

## UEFN

Builds from 24.20 have a matching UEFN release, paired by changelist, installed into the same folder.
You are offered it after a download, or add it later from **My builds**. Turn the prompt off in
**Settings**.

## Several windows

Downloads started from different windows queue up instead of fighting over bandwidth and files. A
waiting window shows what is downloading, how far along it is and your place in line, and starts by
itself when its turn comes. Press esc to leave the queue. If two windows target the same folder, the
second only fetches whatever the first did not already put in place.

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
