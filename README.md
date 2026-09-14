# Jellyfin Email Digest

Sends scheduled multilingual email digests of newly added media to each Jellyfin
user, filtered strictly by what that user is authorized to see in Jellyfin.

Replaces a standalone notifier (Docker container + JSON user mappings + cron/systemd
timer + separate web UI) with a native Jellyfin plugin.

**Status: Phase 2 (persistence and configuration) complete.** No SMTP sending, no media
querying, and no digest orchestration yet — see Phases below.

## Persistence and secrets

- Global configuration (SMTP settings, digest settings, monitored libraries) is
  persisted through Jellyfin's built-in plugin configuration mechanism
  (`IXmlSerializer`), which writes **plain, unencrypted XML** to the server's plugin
  configuration directory. Jellyfin provides no encrypted secret store for plugins as
  of 10.11.8. The SMTP password field exists in the data model already but is
  deliberately **not** exposed on the configuration page yet — the generic plugin
  configuration API has no per-field masking, so rendering it now would return the
  password to the browser in plain text on every page load. A masked, purpose-built
  endpoint for SMTP fields is added in Phase 5, alongside SMTP sending itself.
- Global configuration is validated before every save (`Plugin.UpdateConfiguration`
  override): an invalid save is rejected and the previous configuration is kept.
- Per-user notification preferences and the digest checkpoint are stored as this
  plugin's own JSON files under the plugin's data folder (which is the plugin's own
  install directory, e.g. `.../plugins/Email Digest_0.1.0.0/`), written with an
  atomic write-temp-then-rename pattern — unlike Jellyfin's own XML config save, which
  writes directly to the target file with no such protection.

## Phases

1. Discovery and skeleton — done.
2. Persistence and configuration — done (this includes global config + validation,
   per-user preference repository, checkpoint storage, and English/Spanish/Galician
   localization resources).
3. Media discovery and access control — not started.
4. Email rendering — not started.
5. SMTP — not started.
6. Complete scheduled digest — not started.
7. User self-service — not started.
8. Packaging — not started.

## Compatibility

- Target server: **Jellyfin 10.11.x**, developed and pinned against **10.11.8**.
- Target framework: **net9.0** (matches Jellyfin 10.11.8's own `global.json`, which
  pins .NET SDK `9.0.0`).
- `Jellyfin.Controller` / `Jellyfin.Model` NuGet packages are pinned to the exact
  server version, `10.11.8`. Per Jellyfin's plugin guidance, a mismatch between the
  package version and the running server's version causes the dashboard to report the
  plugin as `NotSupported`. When moving to a newer 10.11.x patch, bump this
  deliberately and re-verify against a live server before shipping.
- **Jellyfin 12 is explicitly out of scope for this plugin at this stage.**

## Plugin identity

- Name: `Email Digest`
- GUID: `0e2dad96-d957-4026-8abb-0ca78068c5f0`
- Assembly: `Jellyfin.Plugin.EmailDigest.dll`

## Building

```shell
dotnet restore
dotnet build --configuration Release
```

Requires .NET SDK 9.0.x (see `global.json`).

## Running tests

```shell
dotnet test
```

Tests never send real email and never touch a live Jellyfin server; they exercise
plugin logic in isolation (config validation, per-user preference storage, checkpoint
storage, localization) using temp-directory-backed fakes and in-process instances.

## Manual install on a Jellyfin 10.11.8 server

1. Build in Release configuration (above).
2. Create a folder named `Email Digest_0.1.0.0` (or similar) under the server's
   plugins directory (e.g. `/var/lib/jellyfin/plugins` on Linux, or
   `%ProgramData%\Jellyfin\Server\plugins` on Windows).
3. Copy `Jellyfin.Plugin.EmailDigest.dll` (and `.pdb` if you want debug symbols) from
   `Jellyfin.Plugin.EmailDigest/bin/Release/net9.0/` into that folder.
4. Restart the Jellyfin server.
5. In Dashboard → Plugins, confirm "Email Digest" appears and is **not** marked
   `NotSupported`.
6. Open the plugin's configuration page from the dashboard — it should show the
   digest enable switch, default language, and fallback lookback fields (no SMTP
   fields yet — see "Persistence and secrets" above).
7. In Dashboard → Scheduled Tasks, confirm a task named "Send Email Digest" appears
   under the "Email Digest" category, and can be run manually (it currently logs one
   line and completes immediately; it sends no email and touches no media).

This has not yet been verified against a live 10.11.8 server as part of this pass —
see the Phase 1 report for what was and wasn't verified.
