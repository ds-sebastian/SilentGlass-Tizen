# Upstream notes and evidence boundaries

Reviewed 2026-10-26 UTC through authenticated GitHub reads. These notes summarize
published documentation; this review performed no TV installation, access trial,
reboot or change. Sources are pinned where possible. An issue is a mutable author
report, not independent verification or a substitute for the linked source code.

Use the [agent walkthrough](agent-workflow.md) and
[access/persistence comparison](access-and-persistence.md) alongside these notes.
Retain upstream project/tool/command names and license notices exactly. “Local TV
maintenance” describes the owner's purpose; UID 0, vulnerabilities, authentication
and firmware restrictions must still be identified accurately where relevant.
Do not rename components to conceal what they do or promise that any AI provider
will accept a task based on its vocabulary.

## Reviewed revisions

| Repository | Revision | How to use it here |
| --- | --- | --- |
| `invaliddayta/s90c-tools` | `599aabc5c22dd424beaadcba7cc94bc204476d29` | External command helper and useful bounded-operation/recovery discipline; S90C cleanup profile is not automatically an S90D profile |
| `chris-ritsen/samsung-tv-root` | `3b97edf3762db7670585d73e29623dc56195bf98` | Access mechanism, exact-model compatibility reports and host-controller design |
| `splitcipher/couchroot` | `fd8a945588786c4af0c859064dbf52266be7091a` | Separate administration-service design, protocol and acceptance/recovery limits |
| `jss8649/quiet-tizen` | `cf518d7a9278dca7990e27756218258e44044a29` | Original U8000F cleanup/launcher/rollback framework |
| `levkropp/samsung-tv-root-untethered` | `2b0629381b3eaf5a78f84332d630d6d4b10681e7` | Related boot-agent research linked from chris-ritsen issue #1; not an S90D deployment recipe |

The revision table is a review snapshot, not a requirement to replace this fork's
vendored code or update live tools to those revisions.

## s90c-tools: useful operational tips

- Target selection is explicit (`--tv`, environment or local configuration), with
  no discovered/default TV. A full probe and UID check are required; a handshake
  alone does not establish administrative access.[1]
- The helper uses temporary staging, Samsung's signed .NET host and an archive
  launcher in a private mount namespace to execute UID-0 jobs. It does not rewrite
  global account/key files or install a custom TV boot service. Its availability
  after reboot comes from rerunning acquisition, not from permanent root.[1]
- Command deadlines are bounded; stdout/stderr are separate and capped. A run-once
  guard and authenticated cached-result retrieval help avoid duplicate execution.
  Inspect diagnostics and effects after transport loss; never assume replay is safe.[1]
- The S90C app-restoration guard changes one app-disable field and refreshes two
  metadata caches. Upstream reports 45 of 46 removed apps stayed absent, while
  Spotify returned for an unknown reason. This is not a universal installer block
  or a guarantee for our firmware.[2]
- The full cleanup apply workflow has offline tests but no end-to-end hardware
  acceptance in that document. Its package-manager service restart and update
  preferences must not be copied blindly; review the actual device services and
  security-update tradeoff first.[2]

## samsung-tv-root: compatibility and host-managed access

- Root results are exact-model/firmware reports; “reported,” “candidate” and
  owner-tested routes have different evidentiary weight. Its README identifies
  downstream adaptations rather than claiming all tools work unmodified.[3]
- The archive route is vulnerability-based privileged access; upstream identifies
  `SVE-2025-50109` for the package-name entry point. Developer Mode authorization
  is necessary context, but it is not itself a general-purpose root shell.[3]
- The host controller keeps temporary access available without a TV-side boot
  configuration change, monitors the configured TV and uses bounded reacquisition.
  Its concrete model paths and policies need compatibility review before S90D use.[4]
- Root and disabling native-execution policy are separate controls. Upstream says
  root does not require disabling UEP for its managed components and leaves the
  policy setting false unless needed. Do not broaden permissions by default.[4]

## Couchroot: management rather than stronger privileges

- The administration service separates a signed service, a native acquisition path
  and a maintained worker. It remains subject to SMACK and firmware controls.[5]
- Mutual TLS, pinned identities, bounded operations, retained jobs and recovery
  records are its main potential benefits over per-command access. It is not a
  permanent unrestricted root account.[5][6]
- A single authenticated worker and a pending-request allowance do not establish
  parallel command execution. A command timeout/lost reply can be unknown outcome;
  managed jobs, ordinary commands and boot survival are separate concepts.[6]
- The S90D 0.7.54 reboot results are author-reported acceptance, not acceptance
  on this owner's TV. Startup-registration correction was needed; fresh-user,
  interrupted-upgrade and physical/standby/endurance gaps remain explicit.[7]
- Current package installation requires existing committed native selection/state.
  Older managed-worker/archive assemblies are removed from new packages; do not
  splice historical migrations into the current procedure.[5]
- A newer optional MQTT bridge is described as discovery/strict command refusal,
  with hardware controls unavailable and no hardware qualification. Do not promise
  a working Home Assistant control integration from its presence in source.[5]

## Quiet Tizen upstream: narrow changes and narrow rollback

- Keep cleanup groups owner-selected. An app record is not a package, optional
  features are not automatically trackers, and source-specific IDs are not a
  cross-model blacklist.[8]
- Fresh snapshots, exact-target guards, preserved unrelated fields, native cache
  readback and verified stop conditions are more reusable than any particular
  service list. Cached preload settings can be restored by vendor refresh.[8]
- Preserve original recovery copies and Developer Mode access. Reconcile partial
  changes, restore only the selected fields/files and keep a Samsung Home escape
  path; factory reset is not a guaranteed first-line rollback.[9]

## Related Q60B boot-agent report

The linked boot-agent project reports autonomous reacquisition on an exact Q60B
firmware using an automatically launched .NET app and local access chain. This is
useful architectural research, not proof that toggling one flag makes an S90D
maintenance service safe or compatible.[10][11]

Its reviewed bridge sends a token in cleartext on the LAN; do not equate that
transport with Couchroot's certificate-authenticated HTTPS endpoint or expose it
outside a trusted environment.[10]

We do not reproduce its donor-app replacement or boot-time acquisition procedure
as a generic recipe. Assess actual privileges, signing/runtime requirements,
independent recovery and normal viewing before considering such a separate trial.

## Claims agents must not carry forward

- “Root once means no acquisition mechanism runs again.” Not for these on-demand
  or boot-reacquisition designs.[1][5][10]
- “A writable directory proves arbitrary root boot units will load there.” Existing
  vendor stop markers establish only the specific conditions inspected locally.
- “An enabled/on-boot manifest proves unattended startup.” Upstream S90D registration
  is a concrete counterexample.[7]
- “More pending requests means more independent root sessions.” Scheduling and
  safety still require verification.[6]
- “A same-series compatibility report validates all sizes, regions and builds.”
  Upstream explicitly distinguishes those scopes.[3]

When an upstream change is adopted, record the new commit, check the relevant
source and tests again, and distinguish documentation review, offline checks,
live readback, owner-observed behavior and reboot/standby acceptance.

## Sources

[1] https://github.com/invaliddayta/s90c-tools/blob/599aabc5c22dd424beaadcba7cc94bc204476d29/docs/usage.md — invaliddayta/s90c-tools / docs/usage.md
[2] https://github.com/invaliddayta/s90c-tools/blob/599aabc5c22dd424beaadcba7cc94bc204476d29/docs/debloat.md — invaliddayta/s90c-tools / docs/debloat.md
[3] https://github.com/chris-ritsen/samsung-tv-root/blob/3b97edf3762db7670585d73e29623dc56195bf98/README.md — chris-ritsen/samsung-tv-root / README.md
[4] https://github.com/chris-ritsen/samsung-tv-root/blob/3b97edf3762db7670585d73e29623dc56195bf98/docs/user/CONTROLLER.md — chris-ritsen/samsung-tv-root / docs/user/CONTROLLER.md
[5] https://github.com/splitcipher/couchroot/blob/fd8a945588786c4af0c859064dbf52266be7091a/agent/README.md — splitcipher/couchroot / agent/README.md
[6] https://github.com/splitcipher/couchroot/blob/fd8a945588786c4af0c859064dbf52266be7091a/agent/PROTOCOL.md — splitcipher/couchroot / agent/PROTOCOL.md
[7] https://github.com/splitcipher/couchroot/blob/fd8a945588786c4af0c859064dbf52266be7091a/docs/project/release-readiness.md — splitcipher/couchroot / docs/project/release-readiness.md
[8] https://github.com/jss8649/quiet-tizen/blob/cf518d7a9278dca7990e27756218258e44044a29/docs/03-cleanup.md — jss8649/quiet-tizen / docs/03-cleanup.md
[9] https://github.com/jss8649/quiet-tizen/blob/cf518d7a9278dca7990e27756218258e44044a29/docs/09-recovery.md — jss8649/quiet-tizen / docs/09-recovery.md
[10] https://github.com/levkropp/samsung-tv-root-untethered/blob/2b0629381b3eaf5a78f84332d630d6d4b10681e7/boot-agent/README.md — levkropp/samsung-tv-root-untethered / boot-agent/README.md
[11] https://github.com/chris-ritsen/samsung-tv-root/issues/1 — chris-ritsen/samsung-tv-root / issue #1
