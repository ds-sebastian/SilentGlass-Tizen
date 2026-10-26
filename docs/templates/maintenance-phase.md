# Maintenance phase record

Copy this template into an ignored `.local/` phase directory. Fill it with actual
observations; leave unchecked/unknown items explicit. Do not commit the completed
private record without sanitizing it.

## Goal, authorization and preserved behavior

- Phase ID / start time:
- Owner-requested outcome and authorized scope:
- Retained features (HDMI/HDR/audio/CEC/settings/apps/OLED care/network/etc.):
- Known viewing or other-session constraints:
- Read-only / live trial / persistent change / removal:

## Identity and evidence baseline

- Selected device track and verified target (private):
- Exact model / reported firmware / internal build:
- Architecture / runtime / relevant security labels and permissions:
- Boot ID and uptime:
- Repository commit and external helper revision:
- Native app/unit/setting readbacks and evidence files:
- Home handler / startup handler / app-onboot registration:
- Active device coordinator and lock ownership:

## Expected diff and inverse

- Exact targets and fields/files allowed to change:
- Dependency/trigger mechanism inspected:
- Untouched fields/files/features:
- Consistent original backup paths and hashes (host and TV):
- Targeted inverse and its guards:
- Independent recovery route if the normal interface fails:
- Necessary owner decisions still missing:

## Execution and reconciliation

- Reviewed script/plan digest:
- Job/request identity and start/end time:
- stdout / stderr / exit status:
- Actual observed diff versus expected diff:
- Result: not started / completed and verified / failed / unknown outcome:
- Partial effects inspected before any retry:
- Independent post-change readback:

## Acceptance

- [ ] Exact-target metadata/files/permissions verified.
- [ ] Native/runtime state agrees with stored state, or discrepancy is explained.
- [ ] Owner confirms relevant normal viewing and controls.
- [ ] Access-job cleanup completed before any restart.
- [ ] Full restart: new boot ID recorded; intended firmware unchanged.
- [ ] Late startup checked with elapsed uptime and relevant logs.
- [ ] Standby/wake checked separately.
- [ ] Recovery/restore actually exercised, or explicitly marked untested.
- [ ] Any package-update/certificate/periodic drift check is documented.

Record each failure, untested item and timing limit separately. Do not mark a phase
fully accepted just because its script returned zero or its unit is enabled.

## Handoff

- Current state and unresolved questions:
- Retained backup/inverse and exact restore scope:
- Next justified step, if any:
- Sanitized repository documentation updated:
- No credentials, original vendor binaries, raw registry dumps or identifying logs
  included in commits/issues:
