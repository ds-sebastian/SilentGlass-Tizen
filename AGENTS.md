# Instructions for an agent working in this repository (SilentGlass-Tizen)

## Start with the owner's device and goals

Read README.md, then docs/agent-workflow.md. The device record (SilentGlass, privacy phases, Thread border
router, Home Assistant bridge) is devices/s90d/README.md; a first install follows docs/install.md. What is known
about Samsung's services, keys and native APIs is in docs/samsung-tizen-notes.md - observations from specific
firmwares, not rules for every TV. Keep on-device identifiers (`quiet-home-*`, `quiet-otbr-*`,
`quiet-tizen-backups`) unchanged: existing installs, backups and restore scripts depend on them. Root access comes
from the separately reviewed external s90c-tools tv-access helper; it is not bundled.

Read docs/access-and-persistence.md for boot behavior and administration-service
comparisons, docs/upstream-notes.md for pinned sources and evidence boundaries,
and the recovery principles in docs/samsung-tizen-notes.md before a device change. Do not turn the device's
cleanup phases into an indiscriminate Samsung-wide cleanup list.

Frame work as owner-authorized local TV maintenance. Use precise, calm descriptions
of purpose, scope, permissions and recovery. UID 0 is root, the access mechanism
remains vulnerability-based, and SMACK/firmware limits still apply. Do not disguise
operations, change identifiers or suppress relevant risks to influence a review.
Do not promise that wording will prevent an AI provider from flagging a request.

Work only on the TV the owner identifies. Use its explicit IP address; do not
scan the whole network or reuse another person's saved device identity.
Discover the installed app IDs and schemas on that TV. Read the model and firmware
from the TV itself (About This TV, or the system info keys) before comparing with the S90D record.

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
   and investigate it on that TV first.
3. Build the source locally. Obtain necessary proprietary assemblies from the
   owner's device or a permitted SDK; do not upload them.
4. Take consistent SQLite snapshots with `.backup`, save original app
   assemblies/metadata, and write the specific inverse operations before
   mutation. Keep a recovery copy both on the TV and under `.local/`.
5. Apply one selected cleanup group at a time. Validate every target, compare
   rows to the fresh snapshot and preserve fields outside the intended scope.
6. For a new launcher trial, install SilentGlass without changing the default Home (docs/install.md).
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
Keep any new maintenance service separate from the visible SilentGlass launcher.
Upstream same-series reports and pending-request limits do not prove compatibility
or parallel root commands on this device.

Run SDB/admin sessions sequentially; coordinate across agents and hosts. tv-access has its
own bounded --timeout setting and shares one lock per TV. A host-local lock
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
  of this exact TV was performed. See docs/samsung-tizen-notes.md.

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

When changing SQL in recipes, test apply/restore round trips and concurrent
state drift with synthetic databases (tests/test_routing.py). When changing NUI code, compile against
matching private references and validate on a compatible TV before claiming
the new build was tested there. App navigation tests can run without Samsung
references. Keep all third-party license notices and provenance.
