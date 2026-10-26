# Contributing to SilentGlass-Tizen

Useful contributions are new compatibility reports, corrections to observed
behavior, small reviewed recipes and improvements to the native launcher.

For a new model, report its exact model/firmware, architecture/runtime, which
steps were exercised, what was checked after full boot and standby/wake, and
how restoration was verified. A successful preflight is not a successful
root session; a root session is not a validated launcher install.

Record another model in its own `devices/<model>/` folder rather than editing
the S90D record, and add what you learn about Samsung's services to
[Tizen notes](docs/samsung-tizen-notes.md) with the firmware it was seen on. Preserve the owner-selected features and take fresh rollback state.
Include sanitized observations and the corresponding tests.

Do not attach serial numbers, MAC/IP addresses, device/account IDs, original
app assemblies, SDK binaries, database snapshots or raw session logs.
Review screenshots for viewing history, profile names and other personal
information before sharing them.

For agent-led changes, follow [the walkthrough](docs/agent-workflow.md), cite
pinned upstream sources when adding factual advice, and distinguish documentation
review, offline checks, live observations and restart/standby acceptance. Use plain
maintenance language without concealing required privileges or security limits.

For local checks:

```sh
python3 -m unittest discover -s tests -v
dotnet run --project tests/CatalogTests.csproj -- launcher/assets/apps.json
```

The S90D inventory comparison and optional local-catalog test skip when ignored
private inputs are absent. Do not upload device inventories or synthesize observations
to make those checks pass. The public catalog/assets and offline logic tests remain
required. Running the test suite does not contact a TV.

NUI changes also need a build against matching private references and a
clearly stated hardware-test status. Keep attribution/license files with
derived assets and code.
