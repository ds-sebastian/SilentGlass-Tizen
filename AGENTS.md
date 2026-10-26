# Instructions for an agent working in this repository (SilentGlass-Tizen)

## Start with the owner's device and goals

Read README.md, then docs/10-agent-workflow.md. Choose the device track before
running commands: the S90D record (SilentGlass Home, privacy phases, Thread border router) is
devices/s90d/README.md; docs/01 through 09 and docs/u8000f-case-study.md preserve the original Quiet Tizen
U8000F case study. Keep on-device identifiers (`quiet-home-*`, `quiet-otbr-*`, `quiet-tizen-backups`)
unchanged: existing installs, backups and restore scripts depend on them. The bundled tools/tv.py identity gate
is still U8000F-only. The S90D uses the separately reviewed s90c-tools tv-access
helper; do not substitute another model's firmware declaration to bypass a gate.

Read docs/11-access-and-persistence.md for boot behavior and administration-service
comparisons, docs/12-upstream-notes.md for pinned sources and evidence boundaries,
and docs/09-recovery.md before a device change. Do not turn either device's profile
into an indiscriminate Samsung-wide cleanup list.

Frame work as owner-authorized local TV maintenance. Use precise, calm descriptions
of purpose, scope, permissions and recovery. UID 0 is root, the access mechanism
remains vulnerability-based, and SMACK/firmware limits still apply. Do not disguise
operations, change identifiers or suppress relevant risks to influence a review.
Do not promise that wording will prevent an AI provider from flagging a request.

Work only on the TV the owner identifies. Use its explicit IP address; do not
scan the whole network or reuse another person's saved device identity.
Discover the installed app IDs and schemas on that TV. Read firmware from
About This TV; the helper's --firmware value is an operator declaration,
not a value it has independently read. SDB's reported model is checked.

Use the owner's current authorization and feature preferences. Ask only for
missing decisions or observations that matter. Do not invent extra approval
steps for reversible work already requested. Default preservation: HDMI,
display, audio, networking, Bluetooth, package/application management,
accessibility and the owner's streaming apps. Voice, SmartThings, health,
gaming-hub helpers and TV Plus are optional features, not automatically malware.

## Work in phases

1. Inventory first; record model, firmware, runtime, schemas, installed apps,
   source IDs, admin capabilities and current Home/startup selectors.
2. Stop before mutation if model/firmware differ. Explain the concrete gap
   and investigate a separate profile. Never fake the firmware argument.
3. Build the source locally. Obtain necessary proprietary assemblies from the
   owner's device or a permitted SDK; do not upload them.
4. Take consistent SQLite snapshots with `.backup`, save original app
   assemblies/metadata, and write the specific inverse operations before
   mutation. Keep a recovery copy both on the TV and under `.local/`.
5. Apply one selected cleanup group at a time. Validate every target, compare
   rows to the fresh snapshot and preserve fields outside the intended scope.
6. For a new launcher trial, install Quiet Home without changing the default Home.
   Verify visible UI, remote, HDMI, sound and settings first. Change Home/startup
   or install a settings bridge only if that integration is owner-selected; do not
   assume launcher adoption requires disabling the sidebar/browser. On an existing
   S90D deployment, preserve the current routes unless this phase changes them.
7. Test a full restart and standby/wake separately. Re-check app-preload values,
   service conditions and Home selectors. A process disappearing once is not
   proof of persistence.
8. Summarize actual evidence, limitations and how to undo the changes. Use
   docs/templates/maintenance-phase.md privately; keep passed, failed and untested
   checks separate. A timeout/lost reply is unknown outcome until reconciled.

Persistent files/settings, boot-launched user tasks and automatic root reacquisition
are distinct. Home-button routing is not startup routing or app_onboot registration.
A vendor stop-marker directory does not prove arbitrary custom boot units load there.
Keep any new maintenance service separate from the visible Quiet Home launcher.
Upstream same-series reports and pending-request limits do not prove compatibility
or parallel root commands on this device.

Run SDB/admin sessions sequentially; coordinate across agents and hosts. The
bundled U8000F helper has a 30-second command budget. External tv-access has its
own bounded --timeout setting; do not assume identical limits. A host-local lock
is not a distributed device lock. Never bypass a busy lock or interrupt a radio,
package or launcher operation to run a background sampler.

A failed script may have completed earlier filesystem operations;
inspect before retrying. Do not overwrite the first backup to make a retry
succeed. Do not reboot or put the TV in standby inside the temporary admin
session; allow its cleanup checks to complete first.

## Implementation constraints

- Do not flash TV firmware, replace a bootloader, remove partition contents,
  alter `/etc/passwd` or replace Samsung's SDB verification key. The one
  exception is the S90D Thread radio module's application image, switched only
  between images Samsung ships on the TV, with Samsung's flasher and the owner's
  approval (devices/s90d/thread-border-router.md).
- Do not assume UID 0 defeats SMACK, signed-native-executable enforcement or
  secure boot. No custom OS installation path was established for this model.
- Preserve native advanced/network/Bluetooth settings for functions the new
  UI does not implement. Never globally disable `org.tizen.menu`.
- Private Samsung APIs, HDMI source integers, UID 5001, owner 201:0, package
  containers and app IDs are observations for the tested build. Inspect them
  before reuse. Bixby Promotion is repurposed; it must not be on the disable list.
- Do not replace a live registry wholesale with an old database. Generate
  field-specific rollback; fail on drift instead of overwriting app updates.
- Do not kill every process, clear streaming-app data, log users out, remove
  account credentials or disable updates as a generic speed fix.
- Runtime systemd masks do not survive restart. Cached app-preload settings
  can be overwritten. No hidden watchdog or always-on root daemon is included.
- Do not claim every privacy path is eliminated. No decrypted traffic audit
  of this exact TV was performed. See docs/08-privacy.md.

## Local data and publication

Use `.local/` for device-specific plans, raw command output, screenshots,
snapshots, builds and extracted framework assemblies. Use the ignored
`launcher/assets/app-icons/` and `apps.local.json` for downloaded publisher art
and customized catalogs. These paths are not for public commits.

Before any public commit, inspect tracked files for credentials, network
addresses, device/account identifiers, personal filesystem paths, screenshots,
logs, proprietary binaries and original database snapshots. Do not upload
an entire working directory. Keep only sanitized findings and minimal
reproduction examples in reports.

When changing SQL generators, test apply/restore round trips and concurrent
state drift with synthetic databases. When changing NUI code, compile against
matching private references and validate on a compatible TV before claiming
the new build was tested there. App navigation tests can run without Samsung
references. Keep all third-party license notices and provenance.
