# Public beta distribution

## Goal

Publish Farever Translate as a free, public, MIT-licensed community project at
`nachthelad/farever-translate`. The first download is a lightweight Windows
x64 ZIP tagged `v0.1.0-beta.1`. It remains a draft until that exact GitHub
artifact has been tested with Farever and cleanly uninstalled.

The project is unofficial, unaffiliated with Shiro Games, intended for
personal use, and provided without a guarantee that the game permits it. It
reads game memory only, never automates input, and translates only a line
explicitly selected by the user.

## Package

The release asset is
`FareverTranslate-v0.1.0-beta.1-win-x64.zip`. It contains:

- the x64 `dinput8.dll` proxy, linked with the static C++ runtime;
- a framework-dependent, single-file `FareverTranslate.Host.exe`;
- Spanish `Instalar.cmd` and `Desinstalar.cmd` entry points;
- internal PowerShell install and uninstall scripts;
- `LEEME.txt` and the MIT `LICENSE.txt`.

It does not contain PDB files, logs, API keys, caches, Farever files, or a copy
of the minimap. Users must have .NET 8 Desktop Runtime x64. Missing runtime
support produces a clear error and the official Microsoft download URL; the
installer does not download or execute third-party installers.

## Compatibility and location

The installer finds Farever in this order:

1. an explicitly supplied directory or the package directory;
2. Steam libraries containing app ID `3672400`;
3. a Windows folder picker.

It refuses to continue while Farever is running, when `Farever.exe` is
missing, when `hlboot.dat` is not an explicitly supported build, or when
`dinput8.dll` is not an allowlisted minimap. The first beta supports:

- minimap v1.2.3:
  `38888C303AD65F17E2C3B398D6B723750E4B694095A02FDE786DEF10C08F83B6`;
- minimap v1.2.4:
  `2C22ECE52A9F7583AC2AA694BD91BC0B4488A69F216C0C249B4F15422D4BFC66`;
- Farever `hlboot.dat`:
  `7C3CA4DD765B50972B0EB524F82F9943A1D24D9EA52CBBA97C7F87D5D840E7A0`.

Unknown hashes fail closed and are printed for diagnostics. Links point to
the upstream minimap release, but the installer never downloads or
redistributes it.

## Installation, update, and recovery

Fresh installation backs up the minimap byte for byte, moves the installed
copy to `farever-minimap.dll`, copies only project-owned payload files, and
writes a versioned manifest atomically.

Re-running the installer over a valid Farever Translate installation performs
a transactional update. It verifies every existing project file, the chained
minimap, and the backup before staging the new payload. Replacement files and
the new manifest are verified before the old files are removed. Any failure
restores the previous version.

Manifest v2 records product version, game build hash, minimap version and
hash, backup and chained paths, and every project-owned file with its SHA-256.
The updater accepts the current private manifest v1 only after verifying all
of its files and migrates it to v2.

Uninstall keeps the existing fail-closed behavior: unexpected or changed
files abort restoration, while a valid installation restores the original
minimap byte for byte.

## Repository and automation

The public repository uses `main`, an MIT license, Spanish documentation, and
no tracked build artifacts. The README covers prerequisites, compatibility,
installation, Google Cloud costs, Alt/F6 controls, recovery, troubleshooting,
and the unofficial-use disclaimer. A screenshot contains no real player names
or chat.

The host and native proxy carry `0.1.0-beta.1` product metadata. The host also
supports `--version` without opening Farever.

GitHub Actions provides:

- Windows CI for the full native/.NET/installer suite;
- CodeQL for C# and C++;
- monthly Dependabot updates for GitHub Actions;
- a tag workflow that retests, builds the exact clean ZIP, creates
  `SHA256SUMS.txt`, and uploads both to a draft prerelease.

Only official actions with pinned revisions and least-privilege permissions
are used. There is no automatic updater.

## Validation and release gate

Automated checks cover the exact ZIP inventory, absence of forbidden files,
single-file version output, proxy exports and runtime dependencies, Steam
location parsing, .NET detection, allowlisted and unknown hashes, v1
migration, transactional update and rollback, tamper rejection, and exact
uninstall restoration.

Before publication:

1. run all tests and whitespace checks;
2. scan tracked history for secrets and accidental binaries;
3. scan the final package with Microsoft Defender;
4. let GitHub create a draft prerelease;
5. download that exact asset;
6. install it with Farever closed and manually validate the minimap, DPS,
   chat, translation, Alt, F6, loads, and zone changes;
7. uninstall and confirm the original minimap hash;
8. publish the prerelease only after all checks pass.

Issue templates request product version, game and minimap hashes, and
technical logs. They explicitly prohibit API keys and chat content. Farever
Early Access updates remain unsupported until their hashes and layouts are
tested and explicitly added.
