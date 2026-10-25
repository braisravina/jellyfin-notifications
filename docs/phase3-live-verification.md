# Phase 3 live verification (manual, reversible)

Status: **not yet performed.** Do not describe Phase 3 as live-verified until this has been
run and the results recorded.

## What can and cannot be checked today

Discovery and library validation are registered with dependency injection but nothing calls
them yet (the scheduled task is still a Phase 1 stub). A live server therefore cannot exercise
discovery end to end until Phase 6. What can be verified now is that the plugin loads and that
its Phase 3 services resolve. The access-control scenarios below should be run at the end of
Phase 6, against the same disposable setup.

## Safety rules

- Use a **separate, disposable Jellyfin container**, never the production one.
- Use the same custom SSE2-compatible image the production host runs. Do not pull, replace or
  upgrade any image.
- Give it its own empty config/cache volumes and a different port, and mount only a small
  throwaway media folder, read-only.
- Do not copy production user data, preferences or checkpoints into it. Create test users.
- Leave SMTP disabled. Phase 3 sends no email.
- The production server's plugins directory, configuration and container are not touched, and
  production is not restarted.

## Part A: plugin loads and services resolve (possible now)

1. Build: `dotnet build --configuration Release`.
2. Start the disposable container, then stop it and create
   `<its config>/plugins/Email Digest_0.1.0.0/`. Copy `Jellyfin.Plugin.EmailDigest.dll` into it.
3. Start the container again.
4. Dashboard -> Plugins: "Email Digest" is listed and is not `NotSupported`.
5. Run the "Send Email Digest" scheduled task once. The log shows the stub line and no
   exception (a failure in dependency injection would show at startup instead).
6. Search the container log for `Email Digest`, `MediaDiscovery` and `InvalidOperationException`:
   there must be no resolution errors.

Rollback: stop and remove the disposable container and its volumes.

## Part B: access control (after Phase 6 makes the task call discovery)

Create libraries `Movies A`, `Movies B`, `Music`, each with a few items, and test users:

| User | Library access |
|------|----------------|
| `all` | all libraries |
| `limited` | `Movies A` only |
| `none` | no libraries |

Configure `MonitoredLibraries` as `Movies A` and `Movies B` only. Add new items to each library,
then run the task and check what each user is told about:

1. `all` gets items from A and B, and nothing from `Music` (not monitored).
2. `limited` gets items from A only. **Nothing from B must appear** (this is the defect the
   access resolver fixes).
3. `none` gets nothing, and the log shows no query was run for that user.
4. Block `Movies A` for `limited` in the user's settings: `limited` now gets nothing.
5. Disable library `Movies A` in its library options: nobody gets items from it.
6. Delete library `Movies B`: the task logs a warning naming the library id and continues; no
   other user's results change.
7. Add a new library `Movies C` and leave the configuration alone: its items appear for nobody.
8. Set a parental rating limit on `limited` and add an item above it: it does not appear.
9. Empty `MonitoredLibraries`: nobody gets anything.
10. Stop the database or make the query fail if feasible: the task reports a failure and the
    checkpoint is not advanced.

Record the Jellyfin version, plugin version, image tag and the result of each step.

## Rollback

Remove the disposable container and volumes. Nothing in production was modified.
