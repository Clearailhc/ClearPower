# Releasing ClearPower

One workflow builds all three platforms and publishes one GitHub Release:
[`.github/workflows/build.yml`](../.github/workflows/build.yml). It runs on every push and pull
request as a build check, and publishes when a `v*` tag is pushed.

## The normal path

**1. Bump the version.** Three files must agree, and the `version` job fails the run if they do
not:

| File | What to change |
|---|---|
| `VERSION` | the bare number, e.g. `0.7.0` |
| `daemon/clearpowerd/__init__.py` | `VERSION = "0.7.0"` |
| `macos/Sources/ClearPowerCore/Version.swift` | `public static let string = "0.7.0"` |

`Version.swift` is also rewritten by `macos/scripts/build-app.sh` at build time, so commit it
anyway — the file in the repository is what the tests read. If a macOS helper must be reinstalled
for the new version, raise `minimumHelperVersion` in the same file; leave it alone when the helper
is compatible (the app then does not prompt).

**2. Write the notes.** Add a `## [<version>] — <YYYY-MM-DD>` section at the top of
`CHANGELOG.md`, and `docs/RELEASE-v<version>.md` for the release body. Both are checked: a missing
changelog section or a missing notes file fails the run before anything is built. The notes file is
what readers see on the Releases page, so lead with what changed for them, and keep a short
scope/validation table like the earlier `docs/RELEASE-v*.md` files.

**3. Verify locally.** You do not have to build all three platforms, but do build the one you can:

```powershell
dotnet test windows/Tests/ClearPowerCoreTests        # 27 tests at the time of writing
pwsh windows/build.ps1                               # build + test + installer + portable zip
```

```bash
python3 -m unittest discover -s daemon/tests -v      # Linux
cd macos && swift test && scripts/build-app.sh       # macOS
```

**4. Commit and push to `main`.**

```bash
git add -A
git commit -m "v0.7.0: <one line>"
git push origin main
```

**5. Tag and push the tag.** This is what publishes.

```bash
git tag v0.7.0
git push origin v0.7.0
```

**6. Watch the run.** The `version` job checks the three version files, that the tag matches
`VERSION`, that `docs/RELEASE-v0.7.0.md` exists and that `CHANGELOG.md` has the section. Then
`linux`, `macos` and `windows` build in parallel and upload their packages; `release` downloads all
three, refuses to continue if any package is for a different version, writes one `SHA256SUMS`, and
publishes the Release with the notes file as the body.

If a job fails, the tag has no Release yet: fix the problem, then move the tag.

```bash
git tag -d v0.7.0 && git push origin :refs/tags/v0.7.0
git tag v0.7.0 && git push origin v0.7.0
```

Deleting the tag does not delete a Release that was already published — delete that from the
Releases page too, or publish a new patch version instead (which is usually the better answer).

## Publishing without a tag push

`Actions → build → Run workflow`, tick **Publish a GitHub Release for the version in VERSION**.
The run tags the commit it built as `v<version>` itself. Useful when you cannot push a tag
locally; the same checks apply, so the version files and notes must already be committed.

## What each job produces

| Job | Runner | Output |
|---|---|---|
| `linux` | `ubuntu-latest` | `clearpower_<version>_all.deb` |
| `macos` | `macos-15` | `ClearPower-<version>.dmg` |
| `windows` | `windows-latest` | `ClearPower-Setup-<version>-x64.exe`, `ClearPower-<version>-x64-portable.zip` |
| `release` | `ubuntu-latest` | one Release with those four packages plus `SHA256SUMS` |

## Notes that have bitten us

- **Artifacts are not committed.** `dist/` and `windows/dist/` are ignored on purpose: the
  repository builds from source, and the Release carries the binaries. Nothing is ever published
  from a working tree — only from the workflow.
- **`windows/build.ps1` checksums only the packages it just built.** It used to checksum every file
  in `windows/dist`, so leftovers from earlier versions were listed in `SHA256SUMS`. The `release`
  job now also refuses to continue if a package for another version reaches it.
- **0.5.0 was tagged but never published** because the macOS job failed on Swift Testing, and the
  Release job was skipped; 0.5.1 shipped the same application. A green build on `main` before
  tagging is the cheap insurance.
- **`EnablePerMonitorDpiAwareness`** is a build property, not an installer property: if Windows
  packaging ever changes, keep it set, or WPF silently ignores the manifest's PerMonitorV2
  awareness again.
