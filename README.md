# Jellyfin Email Digest

Sends scheduled multilingual email digests of newly added media to each Jellyfin
user, filtered strictly by what that user is authorized to see in Jellyfin.

Replaces a standalone notifier (Docker container + JSON user mappings + cron/systemd
timer + separate web UI) with a native Jellyfin plugin.

**Status: Phase 4 (categorization and email rendering) implemented and unit-tested; not
live-verified.** Discovery, the digest model and the renderers exist but nothing calls them
yet: there is no SMTP sending and no digest orchestration. Nothing is ever sent. See Phases
below.

## Media discovery

`MediaDiscoveryService` finds items added to the monitored libraries inside an explicit UTC
window (`fromUtc` exclusive, `toUtc` inclusive) that a given Jellyfin user may see.

- **Categories:** every result carries the stable `LibraryId` it came from and the category
  the administrator configured for that library (`Movies`, `Series`, `Anime`, `Documentaries`,
  `Music`, `Other`). The category comes *only* from that explicit mapping: Jellyfin has no
  Anime library type, so an episode is `Anime` because it sits in a library mapped to Anime,
  never because of its item type or its library's name. Renaming a library changes nothing.
  Libraries are queried once each (not once per item). A library listed twice keeps its first
  category, and an item returned for two libraries is kept once, under the first.
- **Supported media:** movies, episodes and music albums (an album stands in for its
  tracks). Series, seasons, collections, individual tracks and everything else are
  excluded. Results are de-duplicated by item id, and missing metadata stays `null`.
- **Library selection:** `MonitoredLibraries` is an explicit allowlist chosen by an
  administrator. An empty list means nothing is monitored (discovery returns nothing); a
  library added to Jellyfin later is never monitored until it is listed. This is the
  *global* policy and is separate from per-user permissions.
- **Authorization:** a library is queried for a user only if it is both monitored *and*
  accessible to that user. Accessibility is Jellyfin's own `CollectionFolder.IsVisible(user)`
  (library enabled, the user's enabled/blocked folders, parental rating); item-level rules
  (parental rating, blocked tags) come from running the query as that user. The plugin keeps
  no permission map of its own. If no monitored library is accessible, no query runs at all.
- **Library validation:** `MonitoredLibraryValidator` compares the configured ids with
  Jellyfin's libraries and reports invalid (empty), deleted/unknown and duplicate entries. It
  is not wired into saving the configuration yet (Phase 5/6). At discovery time a deleted or
  unknown library is skipped with a warning.
- **Failures:** a Jellyfin failure raises `MediaDiscoveryException` rather than returning an
  empty list, so the checkpoint is never advanced past items that were not examined.
  Cancellation propagates as `OperationCanceledException`.

### Known limitations

- Discovery and validation are registered but not called by anything yet; the scheduled task
  is still a stub.
- Music is reported per album; single audio tracks are not supported.
- `JellyfinLibraryAccessResolver` (the Jellyfin-facing half of access control) has no unit
  tests because `CollectionFolder` needs Jellyfin's static service locator; unit tests use a
  fake of that seam. Its behaviour is only confirmed by live verification.
- Time filtering uses Jellyfin's `DateCreated`. Items re-created in Jellyfin (for example a
  library removed and re-added) look new.
- The behaviour was derived from the public Jellyfin 10.11.8 source and the pinned
  packages. It has not been run against a real server.

Live-verification steps: `docs/phase3-live-verification.md`.

## Digest model and rendering

`DigestBuilder` groups discovered items into a presentation-independent `DigestModel`;
`DigestRenderer` turns it into a localized subject, HTML body and plain-text body. Neither
sends anything, reads or writes checkpoints, or calls Jellyfin.

- **Model:** sections appear in a fixed order (Movies, Series, Anime, Documentaries, Music,
  Other); empty ones are omitted. Movies and albums are one entry each; episodes group under
  their series (series id, else case-insensitive name, else one "unknown series" entry).
  Entries sort by title then id; episodes by season, number, title, id. The same items in any
  order give the same model, and duplicate item ids collapse.
- **Limits:** `DigestOptions` caps entries per section (default 25) and per digest (default
  100). Limits hide whole entries only, and every section reports exact hidden counts for
  "and N more". The digest cap is spent in section order. `DigestModel.Items` always holds
  *every* eligible item, so delivery accounting must use it and never the displayed entries.
- **Localization:** English (`en`), Spanish (`es`) and Galician (`gl`), via `DigestText`. The
  recipient's language is used; a missing or unsupported one falls back to **English**, as does
  any key missing from a language. Plurals use a singular form for exactly 1 and a plural form
  otherwise (right for all three languages). The Spanish and Galician wording has not had a
  native-speaker review.
- **Rendering:** the HTML and text bodies are written from one localized view, so they carry
  the same content and grouping. A series lists at most 10 episodes, then "and N more" (wording
  only). Missing titles, numbers, years and artists become localized placeholders.
- **HTML security:** every dynamic value is first cleaned (control characters and line breaks
  become a space, bidirectional overrides are dropped, whitespace collapses, values over 200
  characters are cut) and then HTML-encoded. The only literal markup is the fixed template:
  inline CSS, no scripts, links, images, fonts, forms, style blocks or external resources, so
  there is no tracking and no tokens in URLs. Item ids, library ids and paths are never rendered.
- **Failures:** rendering errors raise `DigestRenderException`; a caller must treat that as
  "nothing delivered" and not advance the checkpoint.
- **Limitations:** the HTML was verified by tests and by reading it, not in real mail clients
  (no Outlook, Gmail or Apple Mail rendering check); there is no dark-mode styling (fixed light
  colours); the 10-episode cap and the default limits are not yet configurable by the
  administrator; nothing calls the builder or renderer yet.

Golden files in `Jellyfin.Plugin.EmailDigest.Tests/Golden/` hold the exact expected output for
en/es/gl. After an intentional wording or layout change, regenerate them with
`UPDATE_GOLDEN=1 dotnet test` and review the diff before committing.

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
3. Media discovery and access control — implemented and unit-tested (90 tests); **not
   live-verified**. Not yet called by the scheduled task.
4. Categorization and email rendering — implemented and unit-tested (200 tests); **not
   live-verified** and not yet called by the scheduled task. Nothing is sent.
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
storage, localization, media discovery, library validation, digest building and rendering) using temp-directory-backed
fakes, mocked Jellyfin interfaces and in-process instances.

Phase 4 adds tests for category tagging (including Anime versus Series), digest grouping,
ordering and limits, wording in all three languages, HTML escaping and injection attempts,
HTML/text parity, rendering failures and golden files.

Phase 3 test coverage: user-scoped access (inaccessible, deleted and non-library ids; mixed
libraries; different users), empty and duplicate configuration, supported and unsupported
media types, window boundaries, missing metadata, duplicate results, Jellyfin failures and
cancellation. Library validation: valid, empty, invalid, deleted and duplicate entries, and
Jellyfin failure.

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
