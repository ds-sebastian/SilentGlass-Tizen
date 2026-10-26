# Agent walkthrough: owner-authorized TV maintenance

This guide is the starting path for this fork. Work on the owner's explicitly
identified local TV, preserve everyday viewing, and produce evidence rather than
assuming that another Samsung model behaves the same way. Administrative access
means UID 0 (root) where required; it remains subject to Samsung's firmware and
SMACK controls. Describe the scope plainly, not as a general-purpose remote-access
project. Accurate context helps reviewers; euphemisms do not change permissions.

## Choose the correct track first

| Track | Read first | Access tooling | Compatibility boundary |
| --- | --- | --- | --- |
| SilentGlass-Tizen's QN65S90D | [S90D record](../devices/s90d/README.md), then this guide | Separately checked-out `s90c-tools` / `tv-access` | Match exact model, firmware, architecture and runtime; see the S90D record |
| Another TV or firmware | Inventory and a new compatibility assessment | No inherited deployment authorization | A successful access check is not proof of safe cleanup, launcher or boot compatibility |

Never point an example configuration at a guessed device. What is known about
Samsung's services, keys and APIs is collected in [Tizen notes](samsung-tizen-notes.md).
[Upstream notes](upstream-notes.md) distinguish author reports from locally
observed behavior; [persistence](access-and-persistence.md) explains the options.

## Workthrough and required handoff

### 1. Establish scope and a private baseline

- Read `AGENTS.md`, the selected device record, relevant recipes and their inverse.
- Record the owner's desired outcome and features to retain. Keep HDMI, HDR,
  soundbar/CEC/eARC, OLED care, networking, Bluetooth, accessibility, settings and
  package management unless the owner specifically chooses a change.
- Check repository status and active TV operations. One coordinator owns device
  access; other agents can analyze already collected files without touching the TV.
- Keep real addresses, credentials, original binaries, full registry snapshots,
  logs and screenshots under ignored `.local/` paths, not in a commit or issue.
- Privately record exact model/build, boot ID, uptime, runtime, Home and startup
  handlers, relevant unit/app states and installed file hashes. Missing information
  is a gap to investigate, not permission to substitute another model's values.

**Handoff:** identity and feature scope, chosen access path, evidence directory,
current limitations. Do not run cleanup merely while inventorying.

### 2. Confirm the existing access path

For the S90D track, use the separately reviewed `s90c-tools` checkout. Its upstream
helper supports an explicit target, full-path probe and a bounded quoted command;
it is not bundled by this repository.[1]

```sh
# Run from a shell where the reviewed external tv-access helper is installed.
# Supply only the owner's verified TV address; no network discovery is needed.
export TV_HOST=192.0.2.50
# Replace the documentation address above before contacting a device.
tv-access --tv "$TV_HOST" --probe
tv-access --tv "$TV_HOST" 'id -u'
```

Require a verified UID 0 result, not just an SDB handshake; the probe uses
temporary staging even when the requested command only reads state.[1]
Do not mix helper syntax, timeout budgets or retry behavior between access tools.

**Handoff:** actual probe outcome and limits. A failed check calls for diagnosis,
not a different model's access method or weakened identity guard.

### 3. Prepare one reversible phase

- Identify exact targets and dependencies from this device's unit files, app
  records and native readbacks. A suspicious name is not evidence of telemetry.
- Prefer documented settings or verified vendor controls over a new background
  service. Disabling an app, stopping a service and uninstalling a package differ.
- Take consistent SQLite snapshots, original files, metadata and hashes. Prepare
  field-specific rollback that preserves unrelated updates and refuses drift.
- Define success: which fields/files may change; which must remain identical;
  whether normal use, full restart, standby/wake and late-start checks are needed.
- Stay within current owner authorization. New persistent services, access-policy
  changes, package removal, radio changes and viewing interruptions need a clear
  scope decision rather than being hidden inside an unrelated phase.

**Handoff:** reviewed targets, backup/inverse locations, expected diff, verification
plan and any remaining approval. Use the [phase record](templates/maintenance-phase.md).

### 4. Apply once and reconcile failures

Execute one scoped phase through the selected helper, retain stdout/stderr and
exit status, then independently read back exact targets. A timeout or missing reply
is an **unknown outcome**, not proof that nothing happened. Inspect before retrying;
both the external helper and upstream maintenance systems warn against replaying
submitted changes after a lost result.[1][2][6]

Do not delete a first backup, bypass a drift guard or steal another operation's
lock to make a retry work. Preserve Home/startup routing during a launcher trial;
opening a page successfully does not authorize replacing global routing.

### 5. Validate behavior and persistence separately

- Read the changed app records, effective service state, process identities and
  hashes; use native metadata readback when caches can differ from SQL.
- Ask the owner to confirm viewing, HDR, audio/Atmos, CEC, settings and retained
  apps as relevant. A process acknowledgment is not proof of visible behavior.
- With owner agreement, let the access job finish, then perform a real restart.
  Require a changed boot ID and unchanged intended firmware before comparing.
- Wait for late vendor startup and inspect again: the S90D work uses at least
  330 seconds uptime as a practical checkpoint, not a universal completion timer.
  Continue longer if logs show later activation. Test standby/wake separately.
- Recheck after app/package or firmware updates; a working live trial or present
  unit symlink is not sufficient evidence of unattended startup.[7][8]

**Handoff:** passed/failed/untested checks individually, remaining running components,
exact inverse and recovery route. Never label a whole device privacy-complete
from DNS counts or service names alone.

## Troubleshooting without escalating the change

| Symptom | Inspect next | Do not infer or do automatically |
| --- | --- | --- |
| Helper rejects model/firmware | Track selection, operator declaration and observed identity | Do not forge the declaration or widen the gate |
| `holds the local lock` | Local holder and coordinating session | Do not bypass the lock or kill a deployment |
| `closed` / transport error | Exact helper diagnostics, whether a request was submitted, partial effects | Not by itself proof of incompatible firmware; do not replay writes |
| SQL says disabled, native metadata disagrees | Per-device cache/reload method and fresh native readback | Do not copy an S90C service restart onto another build blindly |
| Unit listed disabled but process runs | Dependencies, `.path`/socket activation, vendor launchers | Disabled is not the same as prevented from starting |
| Works live, returns after boot | New boot ID, late targets, app registration and vendor refresh | Do not claim persistence from a one-shot stop |
| Blank launcher after deployment | DLL/resource hashes, labels, cache, logs, Home/startup routes | Do not start editing firmware to fix an app-level regression |
| No binary text or missing tool | `command -v`, stderr, available binary-safe inspection tools | Empty output is not proof that a feature is absent |

The original Quiet Tizen recovery guide explains guarded per-field restoration and
preserving the first backups (summarized in [Tizen notes](samsung-tizen-notes.md#recovery-principles)); those
principles transfer, but device-specific identifiers and execution paths require fresh inspection.[9]

## Offline work, verification and publication

Run checks without contacting a TV:

```sh
python3 -m unittest discover -s tests -v
```

The device-inventory comparison and optional local-catalog checks skip when their
private inputs are absent. Public catalog, assets, routing/cleanup and source-level
checks still run. Do not create pretend TV observations to make tests pass.
The access-component build and .NET catalog checks are described in
[CONTRIBUTING.md](../CONTRIBUTING.md); the NUI build needs matching private references.
A compile or synthetic test is not hardware acceptance.

Before committing: inspect `git diff`, stage only intended files, check local links,
keep third-party attribution, and exclude raw observations, credentials and vendor
binaries. Confirm remote branch/commit after publishing. Report hardware work as
not performed when the task was documentation only.

## Sources

[1] https://github.com/invaliddayta/s90c-tools/blob/599aabc5c22dd424beaadcba7cc94bc204476d29/docs/usage.md — invaliddayta/s90c-tools / docs/usage.md
[2] https://github.com/invaliddayta/s90c-tools/blob/599aabc5c22dd424beaadcba7cc94bc204476d29/docs/debloat.md — invaliddayta/s90c-tools / docs/debloat.md
[6] https://github.com/splitcipher/couchroot/blob/fd8a945588786c4af0c859064dbf52266be7091a/agent/PROTOCOL.md — splitcipher/couchroot / agent/PROTOCOL.md
[7] https://github.com/splitcipher/couchroot/blob/fd8a945588786c4af0c859064dbf52266be7091a/docs/project/release-readiness.md — splitcipher/couchroot / docs/project/release-readiness.md
[8] https://github.com/jss8649/quiet-tizen/blob/cf518d7a9278dca7990e27756218258e44044a29/docs/03-cleanup.md — jss8649/quiet-tizen / docs/03-cleanup.md
[9] https://github.com/jss8649/quiet-tizen/blob/cf518d7a9278dca7990e27756218258e44044a29/docs/09-recovery.md — jss8649/quiet-tizen / docs/09-recovery.md
