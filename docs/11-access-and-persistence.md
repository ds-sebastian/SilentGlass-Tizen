# Access and boot persistence: choose the smallest useful change

This is a comparison and acceptance checklist, not an installer. Keep the owner's
working launcher, normal viewing and independent computer recovery separate from
any new administration service. See the [agent walkthrough](10-agent-workflow.md)
for the phased workflow and [upstream notes](12-upstream-notes.md) for source scope.

## Four different meanings of “persistent”

1. **Stored configuration survives:** a setting, selected app flag, original-file
   backup or verified vendor stop marker remains on writable storage.
2. **A task runs at boot:** an app registration or user-manager unit starts code
   again. This does not establish privileged access or successful completion.
3. **Administrative access returns:** a host controller or on-TV service repeats
   the acquisition mechanism and verifies UID 0 after boot.
4. **A managed job can be recovered:** a maintenance system retains enough state
   to reconcile a restart or lost reply. This does not mean every shell process
   survives a TV reboot or every change executes exactly once.

Do not use one result to claim the other three. The original cleanup guide
explicitly distinguishes writable stop conditions from runtime-only masks and
records vendor refreshes restoring preload preferences.[8]

## Comparing the available approaches

| Approach | Useful for | Main cost / boundary | Evidence |
| --- | --- | --- | --- |
| Current S90D `tv-access` | Occasional diagnostics and bounded maintenance | Host initiates per-command acquisition; no custom boot service installed by the helper | External upstream behavior; this fork's device record identifies the local target.[1] |
| Host controller | Regaining access when the configured TV becomes available, without changing TV boot configuration | Requires host availability and a compatible model path; not an S90D drop-in just because archive access works | Upstream controller guide.[4] |
| Owner user-manager unit | A narrowly scoped boot task using the owner's actual permissions | Not root by default; a running/enabled unit can still fail its task or start too early | Existing S90D scripts in `devices/s90d/device/boot-units/`; validate per phase |
| levkropp boot agent | Studying autonomous on-TV reacquisition | Q60B-specific author proof; donor-app and Developer Mode changes; additional management surface | Boot-agent writeup and issue report, not this owner's S90D acceptance.[10][11] |
| Couchroot administration service | Enrolled maintenance endpoint, managed jobs, files and unattended access | Signed artifacts, certificate lifecycle, boot registration, installation prerequisites and recovery burden | Upstream S90D acceptance is reported, but coverage is incomplete.[5][7] |

**Recommendation:** retain the known-working on-demand path first. Coordinate AI
sessions on the host; do not change the TV just to avoid a lock wait. Investigate
Couchroot's administration component only when autonomous access or managed jobs
justify the added maintenance. Keep it separate from Quiet Home. This is a project
recommendation, not a claim of measured superiority or compatibility.

## Reusing existing device controls

For stored settings and cleanup, inspect the exact vendor service conditions,
app flags, preferences and dependencies. Some vendor units check an absence/presence
marker; others can be started through dependencies even when listed disabled.
The original Quiet Tizen recipes verify exact negated stop-file conditions and
warn that runtime masks clear on restart.[8]

The directory `/opt/usr/share/systemd/system/` containing vendor stop markers is
**not proof** that arbitrary custom systemd units placed there will load. Inspect
actual unit search paths and activation. Similarly, writable `/opt` alone does
not prove that copied executables, setuid bits, capabilities or app launch flags
will work under the current security policy.

For the S90D owner units already in this fork:
- Review the actual installer/update/inverse together; they are historical phases,
  not a batch to run all at once. In particular the Prime janitor was later removed
  by batch D; do not re-enable it just because an older script exists.
- Verify effective user, permissions, labels, dependencies and task logs. A user
  manager is not the system root manager.
- Long waits must not block the owner's session startup; inspect the unit type and
  ordering. A task needs bounded behavior, a narrowly scoped target and a tested
  way to disable it.
- Confirm success during the real late-boot sequence, separately from a live trial.

## What Couchroot adds, and what it does not

At the reviewed revision, Couchroot's on-TV service reacquires a temporary
namespace-root context and connects a maintained native worker. It does not patch
firmware or bypass SMACK; the small .NET bridge invokes the native acquisition
library rather than containing the command/file/job engine.[5]

The documented management layer includes commands, chunked file operations,
managed jobs, health, worker readiness and durable recovery records.[5][6]
Reusing a ready worker could reduce per-command setup overhead, but there is no
local speed comparison. The public HTTPS endpoint requires mutual TLS with pinned
client/server identities; the private loopback worker channel is authenticated.
Private enrollment credentials carry administrative authority.[5]

### Concurrency and uncertain outcomes

Only one authenticated worker is active, and the documented 32 pending JS requests
are **not evidence of parallel EXEC execution or multiple independent root shells**.[6]
Do not promise improved concurrency until the implementation and harmless concurrent
read tests establish scheduling behavior. Serialize registry/deployment/radio changes
regardless of transport.

An EXEC deadline loss closes the channel and leaves the outcome unknown; reconcile
before retrying. Ordinary background shell commands are not retained jobs. The
protocol documents a default 30,000 ms EXEC timeout, a 300,000 ms upper limit,
1 MiB retained output per stream, and bounded chunked transfers; these are limits,
not measured throughput or a guarantee of persistence for every operation.[6]

### Installation and acceptance caveats

- Upstream reports S90D native agent 0.7.54 command/job, retained-job, handoff and
  unattended-reboot checks passed. Interrupted-upgrade, physical UI/standby/endurance
  and fresh-user recovery coverage remain incomplete.[7]
- The current package requires a settled existing native-worker selection and
  committed activation/state records. Missing selection, managed selection and
  unfinished migration are rejected; a fresh installation path must be assessed
  separately.[5]
- The current package is native-only. Historical managed-worker deployment or
  rollback instructions must not be mixed into a new package procedure.[5]
- Upstream's S90D registration had on-boot and auto-restart disabled despite the
  signed manifest. A separately approved targeted correction preceded successful
  reboot acceptance. Current checks detect wrong flags but do not automatically
  repair them or prevent package-manager resets.[7]
- The protocol refactor has its own pending hardware-acceptance qualification;
  older successful agent trials do not validate every newer change.[6]

### Recovery must exist before loopback admission changes

Retain the former Developer Mode Host PC IP, matching checkout, signed recovery
packages, private credentials and immutable journals. If loopback admission blocks
computer access and the service cannot recover, upstream documents restoring the
former Host PC IP through Samsung Apps → Developer Mode and restarting, followed
by verification of normal SDK access. Disabling the agent alone does not restore
SDK admission or undo prior commands.[5]

Do not weaken certificate validation, add an unauthenticated LAN shell, use an old
whole-registry image as rollback, or repurpose the visible launcher as an access
service merely for convenience.

## Minimum acceptance before any new boot mechanism

- [ ] Owner has chosen the goal and exact scope, separately from launcher cleanup.
- [ ] Exact model/build/runtime and supported installation path are established.
- [ ] Original artifacts, metadata, settings and field-specific inverse are retained.
- [ ] Independent computer/remote recovery has been demonstrated, not merely planned.
- [ ] A benign live trial succeeds with expected permissions and no viewing changes.
- [ ] Authentication rejects untrusted clients where a new endpoint is involved.
- [ ] Owner-approved restart proves a new boot ID, startup registration, actual task
      completion and normal viewing after late boot.
- [ ] Standby/wake and the specifically relevant failure cases are checked separately.
- [ ] Package-update drift, certificate renewal and an uninstall/disable path are documented.

Unchecked items stay **untested**. This repository documentation does not authorize
installing any upstream agent or changing Developer Mode admission.

## Sources

[1] https://github.com/invaliddayta/s90c-tools/blob/599aabc5c22dd424beaadcba7cc94bc204476d29/docs/usage.md — invaliddayta/s90c-tools / docs/usage.md
[4] https://github.com/chris-ritsen/samsung-tv-root/blob/3b97edf3762db7670585d73e29623dc56195bf98/docs/user/CONTROLLER.md — chris-ritsen/samsung-tv-root / docs/user/CONTROLLER.md
[5] https://github.com/splitcipher/couchroot/blob/fd8a945588786c4af0c859064dbf52266be7091a/agent/README.md — splitcipher/couchroot / agent/README.md
[6] https://github.com/splitcipher/couchroot/blob/fd8a945588786c4af0c859064dbf52266be7091a/agent/PROTOCOL.md — splitcipher/couchroot / agent/PROTOCOL.md
[7] https://github.com/splitcipher/couchroot/blob/fd8a945588786c4af0c859064dbf52266be7091a/docs/project/release-readiness.md — splitcipher/couchroot / docs/project/release-readiness.md
[8] https://github.com/jss8649/quiet-tizen/blob/cf518d7a9278dca7990e27756218258e44044a29/docs/03-cleanup.md — jss8649/quiet-tizen / docs/03-cleanup.md
[10] https://github.com/levkropp/samsung-tv-root-untethered/blob/2b0629381b3eaf5a78f84332d630d6d4b10681e7/boot-agent/README.md — levkropp/samsung-tv-root-untethered / boot-agent/README.md
[11] https://github.com/chris-ritsen/samsung-tv-root/issues/1 — chris-ritsen/samsung-tv-root / issue #1
