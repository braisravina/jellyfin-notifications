# Jellyfin Email Digest

Sends scheduled multilingual email digests of newly added media to each Jellyfin
user, filtered strictly by what that user is authorized to see in Jellyfin.

Replaces a standalone notifier (Docker container + JSON user mappings + cron/systemd
timer + separate web UI) with a native Jellyfin plugin.

**Status: Phase 6 (scheduled digest orchestration) implemented and unit-tested; not
live-verified.** The scheduled task now discovers, builds, renders and sends a digest per user and
records confirmed deliveries. **Nothing is sent after installing or upgrading**: the digest is
disabled, dry run is on and SMTP is off by default, and no user has notification preferences until
the Phase 7 page exists. See "Scheduled digest" for the guarantees and their limits.

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
  runs when the monitored libraries are saved and when the digest is enabled, and again before
  every digest run (invalid or deleted libraries abort the run). At discovery time a deleted or
  unknown library is skipped with a warning.
- **Failures:** a Jellyfin failure raises `MediaDiscoveryException` rather than returning an
  empty list, so the checkpoint is never advanced past items that were not examined.
  Cancellation propagates as `OperationCanceledException`.

### Known limitations

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
  colours). The limits (entries per category, entries per digest, episodes listed per series) are
  set on the plugin page; the per-digest limit is shared fairly between categories, and entries a
  limit hides stay pending for the user's next digest.

Golden files in `Jellyfin.Plugin.EmailDigest.Tests/Golden/` hold the exact expected output for
en/es/gl. After an intentional wording or layout change, regenerate them with
`UPDATE_GOLDEN=1 dotnet test` and review the diff before committing.

## SMTP delivery

`MailKitEmailTransport` (MailKit 4.16.0) hands one finished message to an SMTP server. It
knows nothing about media, digests, preferences or checkpoints. The digest runner (see
"Scheduled digest") is what connects discovery, rendering and this transport.

### Settings

Configured on the plugin's page in the Jellyfin dashboard (administrators only):

| Setting | Notes |
|---|---|
| Enable SMTP | Master switch for sending through the server below |
| Host | DNS name or IP address, no port or scheme |
| Port | 1-65535; usually 587 for STARTTLS and 465 for implicit TLS |
| Encryption | STARTTLS (default), implicit TLS, or None (unencrypted) |
| Username | Optional; refused together with unencrypted SMTP |
| Password | Write-only, managed separately (below) |
| Sender address / name | Single lines; the address must be a full `user@domain` |
| Timeout | 5-120 seconds (default 30), for connecting and for each step |

All values are validated on save and again before use.

### Encryption behaviour

- **STARTTLS is mandatory, not opportunistic.** If the server does not offer it or the upgrade
  fails, the connection fails with a TLS error and nothing is sent. It never continues in plain text.
- **Implicit TLS** encrypts from the first byte.
- **None** is used only when an administrator selects it. The page shows a warning, and a username
  or password cannot be combined with it, so credentials are never sent in clear text.
- **Certificates are always verified** with the platform defaults, including revocation checks.
  There is deliberately no option to accept an invalid or self-signed certificate. A server with a
  private CA must have that CA installed in the trust store of the machine or container running
  Jellyfin.

### Password storage, threat model and limits

The password is held by `SmtpPasswordStore`, outside the plugin configuration, and is reachable only
through the administrator endpoints, which can set or clear it but have **no way to read it back**.

Sources, in order of preference:

1. **A Docker secret (preferred).** Set `EMAILDIGEST_SMTP_PASSWORD_FILE` to the path of a file
   containing the password (a trailing newline is ignored). It is controlled by whoever deploys
   the container, takes precedence, and cannot be changed or cleared from the web page.
2. **Saved from the page.** Written to `smtp-password.secret` in the plugin's folder, created
   owner-only (`0600` on Unix) and renamed into place.

What this protects against: the password reaching the browser, the generic configuration API,
logs, URLs, error messages or browser storage; a non-administrator reading or changing it; another
site making the browser change it.

What it does **not** protect against, stated plainly:

- **The saved password is plain text on disk.** File permissions restrict who can read it; they do
  not encrypt it. Anyone who can read the plugin folder, a backup or a snapshot of it, or who
  controls the Jellyfin process, can read it. Jellyfin provides plugins no encrypted store, and a
  key kept beside the data would add no real protection.
- A Docker secret is also readable by the Jellyfin process and by whoever can read the secret file
  on the host. It avoids storing the password inside the plugin folder and in its backups, which is
  why it is preferred.
- **Changing the server discards the saved password.** If the host, port or username changes (on
  the plugin page or through Jellyfin's generic configuration endpoint), the saved password is
  removed and must be entered again, so an administrator cannot simply repoint the saved password at
  a server they control. A Docker-secret password is chosen by the deployment and is not affected.
  Anyone who can both change the host *and* knows or supplies a password is, of course, unaffected
  by this: treat plugin administrators as trusted with the SMTP account.
- Use an account used only for this plugin, so a leaked password has a small blast radius.

### Administrator API and access

All endpoints are under `EmailDigest/Admin` and carry Jellyfin's `RequiresElevation` policy: only an
authenticated administrator is accepted. `GET Settings`, `PUT Settings` (no password member),
`PUT Password` (JSON body only), `DELETE Password`, `POST TestConnection` (sends no mail), `POST
TestEmail` (one message to an address the administrator types; at most one per five seconds) and
`GET Strings`. Responses are not cacheable, carry no password and no raw exception text, and report
failures as an outcome code with a localized message.

CSRF: Jellyfin authenticates API calls with a token in a request header, never an ambient cookie,
so another site cannot make a browser send an authenticated request. Nothing that changes state
uses GET, and every action with a body accepts only `application/json`.

### Delivery outcomes

`Accepted` means **the SMTP server accepted the message**. It does not mean the message reached or
was read in a mailbox: it can still bounce later or be filtered as spam.

| Outcome | Typical cause and fix |
|---|---|
| `InvalidRequest` | A setting, address or subject is invalid; nothing was sent. Re-check the settings and that a username has a password and encryption |
| `ConnectionFailed` | Wrong host or port, firewall, or the server is down. From the Jellyfin host or container, test the port with `nc -vz host port` |
| `Timeout` | The server did not answer within the timeout. Check the network, or raise the timeout |
| `TlsFailed` | The server lacks the chosen encryption, the port does not match the mode (587 with implicit TLS, or 465 with STARTTLS), or the certificate is not trusted (self-signed, wrong name, expired). Fix the certificate or install the CA; verification cannot be turned off |
| `AuthenticationFailed` | Wrong username or password, or the server offers no login. App-specific passwords are often required |
| `SenderRejected` / `RecipientRejected` | The server refused the address; the SMTP code is shown. Check the sender is allowed for this account |
| `Cancelled` | The request was cancelled |
| `UnexpectedError` | Anything else; see the Jellyfin log (only the error type is logged) |

Logs record host, port, step, outcome and exception type. They never contain passwords, usernames,
recipient addresses, display names, message content, server replies or exception messages.

### Docker deployment

- Do **not** change the Jellyfin image. The plugin runs in the existing one.
- The plugin folder must contain `Jellyfin.Plugin.EmailDigest.dll` **and** `MailKit.dll`,
  `MimeKit.dll` and `BouncyCastle.Cryptography.dll` (all produced by the Release build).
- Preferred password delivery, as a file-based Docker secret:

  ```yaml
  services:
    jellyfin:
      environment:
        - EMAILDIGEST_SMTP_PASSWORD_FILE=/run/secrets/emaildigest_smtp_password
      secrets:
        - emaildigest_smtp_password
  secrets:
    emaildigest_smtp_password:
      file: ./secrets/emaildigest_smtp_password.txt   # readable only by the Jellyfin user
  ```

  Make sure the file inside the container is readable by the user Jellyfin runs as.
- The container needs outbound access to the SMTP host and port.

### Testing SMTP manually

See `docs/phase5-live-verification.md` (disposable, reversible, no production changes).

## Scheduled digest

`SendEmailDigestTask` (Dashboard -> Scheduled Tasks -> Email Digest, daily at 08:00 by default) calls
`DigestRunner`. A manual "run" from that page takes exactly the same path, so it obeys the same
safeguards.

### Safeguards and modes

| Setting | Default | Effect |
|---|---|---|
| Enable digest emails | off | While off the task does nothing at all (no discovery, no mail, no state) |
| Dry run | **on** | A run builds and renders each user's digest and logs counts, but sends nothing and records nothing |
| Enable SMTP | off | A live run aborts if SMTP is not enabled |

Nothing is enabled by installing or upgrading. To send for real an administrator must enable SMTP,
choose the monitored libraries, enable the digest and turn dry run off. Users must also have
preferences (email address, language, notifications on); Phase 7 adds the page for users to set them.
The plugin page's Status section shows the mode and the last run.

### What one run does

1. Exit if disabled. Take the run lock, or do nothing if another run is in progress.
2. Fix one upper bound for the whole run: now minus five minutes (so media whose creation is still
   being saved is not skipped). Media created later belongs to the next run.
3. Abort before anything is sent or advanced if the configuration is invalid, a monitored library is
   invalid or deleted or cannot be verified, SMTP is disabled or the login has no password (live runs),
   or the delivery state file cannot be trusted. No monitored libraries is a skip, never
   "monitor everything".
4. For each user with notifications on, in id order: skip unknown or disabled Jellyfin accounts and
   invalid stored addresses (their state is untouched); choose the user's language (else the default);
   work out their window; discover media **as that user** (Jellyfin's own permissions); drop items
   already delivered; build and render the digest; send **one** message to that user; and only after the
   SMTP server accepts it, record that user's delivery. If nothing is new, no email is sent and the
   checkpoint moves to the end of the examined window.

### Per-user checkpoints and baselines

Each user has an independent record in `delivery-state.json`: their checkpoint (the exclusive start of
their next window) and the items delivered in the last 90 days. A window is (checkpoint, upper bound]:
exclusive start, inclusive end.

- A successful delivery advances only that user's checkpoint. A failure, an uncertain outcome,
  cancellation or a recording error leaves it where it was. One user's failure never blocks another.
- **New user (no record):** gets only the configured fallback lookback (0-720 hours, default 168),
  never the whole library. With a lookback of 0 the first run sends nothing and only records "from now".
- **Paused user (notifications off):** their record is left alone, so nothing pending is lost. When
  notifications are turned back on they are caught up from their checkpoint, but never further back
  than 30 days; older media is deliberately not announced.
- **Display limits never lose notifications:** entries hidden by the per-category or per-digest limit
  stay pending. The checkpoint moves only to just before the oldest hidden item, items already shown
  are remembered, and the digest tells the user that the rest follows in the next one.
- **Re-imported media:** Jellyfin gives a re-added file the same id with a new creation date. Items
  delivered within the last 90 days are not announced again; after 90 days they would be.
- **Changing the monitored libraries:** windows are time based, not per library. Adding a library
  reports its items created since each user's checkpoint (no backfill of older items); removing one
  simply stops its items being reported.
- A legacy global `checkpoint.json` from before per-user state (no released version wrote one) is only
  read, as a starting point for users with no record. It is never modified, moved or deleted.
- Writes are atomic (temp file then rename) and the previous version is kept as
  `delivery-state.json.bak`. A damaged, newer-format or invalid file stops the run and is never
  overwritten or treated as empty. **Recovery:** stop Jellyfin or wait for no run, then copy
  `delivery-state.json.bak` over `delivery-state.json` (users may then get one duplicate digest), or
  delete the file to treat everyone as new (each gets the bounded lookback).

### Where state lives, and upgrades

State lives in `<server data directory>/EmailDigest/` (`IApplicationPaths.DataPath`; `/config/data/EmailDigest/` in
the standard container), **outside** the `plugins/` tree. Installing, replacing, disabling or removing a plugin
version therefore never touches it. Earlier versions used `plugins/Jellyfin.Plugin.EmailDigest/` (Jellyfin's
per-plugin data folder), which is inside the tree Jellyfin's plugin manager installs into and cleans.

On the first start of this version the files below are **copied** from the old folder (never moved, changed or
deleted), each to a temporary name, checked by SHA-256 and renamed into place; a completion marker
(`storage-migration.json`) is written last. An interrupted migration simply runs again. A file that already exists
in the new folder is never overwritten. If the copy cannot be completed the plugin refuses to use state at all
(it never starts from empty state). **Downgrade:** the old folder still holds the state as it was at the upgrade,
so an older version starts from that (changes made after the upgrade are in the new folder only). **Upgrading
again** keeps the new folder and does not re-import the older copy. Files: `delivery-state.json` (+ `.bak`),
`checkpoint.json` (legacy hint, read only), `last-run.json`, `user-preferences.json`, `smtp-password.secret`;
`digest-run.lock` is created fresh. New files are created readable by the server's account only.

### Failures, retries and uncertainty

| Result | Reaction |
|---|---|
| Accepted by the SMTP server | Record the delivery for that user |
| Connection failed, timeout, or a 4xx reply | Retry, at most 3 attempts in total (waits of 2 s then 10 s), then fail that user |
| Recipient refused (5xx) or invalid request | Fail that user, no retry; others continue |
| Login failed, TLS failed, sender refused | Stop the whole run: every user would fail the same way and retrying risks locking the account |
| **Uncertain** (connection lost after sending began) | Not retried now, not recorded; a later run may resend |
| Cancelled | Stop; nothing for the current user is recorded |

There are no background retries: whatever is left is picked up by the next scheduled run.

**This is at-least-once delivery, not exactly-once.** SMTP cannot offer exactly-once. If the server accepts a
message and the reply is lost, or if recording a confirmed delivery fails, the next run resends and the user
may receive a duplicate. A resend of the same digest carries the same Message-ID (a hash of user, window and
items) and each attempt has its own id in the log, which helps diagnosis; nothing assumes a mail provider
deduplicates on it. A write failure after acceptance stops the run and is reported as "accepted but not
recorded".

### Concurrency

One run at a time. The lock is an in-process lock per data folder plus an exclusive operating-system lock on
`digest-run.lock`, so scheduled and manual runs, and even two Jellyfin processes sharing the folder, cannot
overlap. The lock is the open file handle: a crash releases it, and a leftover file does not block anything. If
a run is already in progress the new one does nothing and says so. Jellyfin running as several instances against
one shared folder is not a supported deployment.

### Reporting

Every run logs phases and counts and saves a summary (`last-run.json`: counts only, no names, addresses, user
ids, titles or content). A run with any failed or uncertain user, or one that stopped early, ends
`CompletedWithFailures` or `Aborted`, is logged as a warning, and makes the scheduled task **fail** in Jellyfin's
task list (the message holds counts and a reason code only). It is never shown as a plain success.

### Known limitations of Phase 6

- Not live-verified; see `docs/phase6-live-verification.md`.
- No user can set preferences until Phase 7, so no real digest can be delivered until preferences are seeded by
  hand or the Phase 7 page exists.
- At-least-once delivery (above), and a 5-minute settle margin means media appears in the next run after it.
- The deleted-library check aborts the whole run; fix the monitored libraries to resume.
- Jellyfin's task list shows a failed task but not the per-user breakdown; use the log and the Status section.
- A paused user is caught up at most 30 days.
- Delivery state for users deleted from Jellyfin is kept (it is small).

### Phase 7 prerequisites

- A user-facing page (or API) to set email, language and notifications, authenticated as that user and able to
  change only their own preferences. The runner reads `user-preferences.json` and writes nothing to it.
- Decide how an address is verified, since a typo or a hostile address sends mail to a third party.
- A way for a user to see or reset their own delivery status if wanted.
- Unsubscribe handling: the digest currently has no link, by design (no tokens in URLs).

## Persistence and secrets

- Global configuration (SMTP settings, digest settings, monitored libraries) is
  persisted through Jellyfin's built-in plugin configuration mechanism
  (`IXmlSerializer`), which writes **plain, unencrypted XML** to the server's plugin
  configuration directory, and Jellyfin's generic `GET /Plugins/{id}/Configuration`
  returns that object verbatim as JSON. It therefore holds **no secret**: the SMTP
  password is not part of it (see "SMTP delivery").
- Global configuration is validated before every save (`Plugin.UpdateConfiguration`
  override): an invalid save is rejected and the previous configuration is kept.
- Per-user notification preferences and per-user delivery state are stored as this plugin's own
  JSON files under `<server data directory>/EmailDigest/` (see "Where state lives, and upgrades"), written with an atomic
  write-temp-then-rename pattern, unlike Jellyfin's own XML config save, which writes directly
  to the target file with no such protection.

## Phases

1. Discovery and skeleton — done.
2. Persistence and configuration — done (this includes global config + validation,
   per-user preference repository, checkpoint storage, and English/Spanish/Galician
   localization resources).
3. Media discovery and access control — implemented and unit-tested (90 tests); **not
   live-verified**. Not yet called by the scheduled task.
4. Categorization and email rendering — implemented and unit-tested (200 tests); **not
   live-verified** and not yet called by the scheduled task. Nothing is sent.
5. Secure SMTP delivery — implemented and unit-tested (386 tests, including a local fake SMTP
   server); **not live-verified**. Sends nothing except an administrator-requested test message.
6. Scheduled digest orchestration — implemented and unit-tested (588 tests); **not live-verified**.
   Disabled and dry-run by default; no preferences exist until Phase 7.
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
storage, localization, media discovery, library validation, digest building and rendering,
SMTP transport and the administrator API) using temp-directory-backed
fakes, mocked Jellyfin interfaces and in-process instances.

Phase 6 adds tests for the digest runner against real storage in a temporary folder with fakes for
Jellyfin and the network (nothing is sent): per-user checkpoints, baselines, retries and failure
classes, uncertain outcomes, cancellation, overlapping runs, stale locks, restart recovery, library
and configuration aborts, hidden-entry carry-over, re-imports, dry run, logging without personal
data, and the scheduled task and status endpoint. A mutation check confirmed that letting a failed
delivery advance a checkpoint fails the suite.

Phase 5 adds tests for SMTP validation, the password store and lifecycle, the absence of any
secret in the configuration's JSON and XML, the transport against a disposable in-process SMTP
capture server on loopback (nothing leaves the machine; no real mail is sent), TLS enforcement and
downgrade prevention, authentication and recipient errors, timeouts and cancellation, MIME
structure and UTF-8, per-recipient isolation, log redaction, the administrator service and the
controller's authorization declarations, and static checks of the administrator page. They cannot
prove that Jellyfin enforces the authorization attribute or that the page works in the dashboard;
that needs the live verification.

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
3. Copy `Jellyfin.Plugin.EmailDigest.dll`, `MailKit.dll`, `MimeKit.dll` and
   `BouncyCastle.Cryptography.dll` (and `.pdb` if you want debug symbols) from
   `Jellyfin.Plugin.EmailDigest/bin/Release/net9.0/` into that folder. The last three are
   the SMTP library; without them the plugin cannot send.
4. Restart the Jellyfin server.
5. In Dashboard → Plugins, confirm "Email Digest" appears and is **not** marked
   `NotSupported`.
6. Open the plugin's configuration page from the dashboard — it should show the digest
   settings, the SMTP server settings, a password section and the tests (see "SMTP delivery").
7. In Dashboard → Scheduled Tasks, confirm a task named "Send Email Digest" appears
   under the "Email Digest" category, and can be run manually (it currently logs one
   line and completes immediately; it sends no email and touches no media).

Only the Phase 1 skeleton has been verified against a live 10.11.8 server. Nothing added
since, including the administrator page and its API, has been run on a real server; see
`docs/phase3-live-verification.md` and `docs/phase5-live-verification.md`.
