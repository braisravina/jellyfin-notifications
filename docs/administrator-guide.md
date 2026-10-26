# Email digest: administrator guide

For the person who runs the Jellyfin server. See `README.md` for the design, `docs/security.md` for the threat
model, `docs/user-guide.md` for what users are told, and `docs/phase7-live-verification.md` for the disposable
test you should run before sending real mail.

## What a fresh install does

Nothing is sent. The defaults are: digest **off**, dry run **on**, SMTP **off**, no public address, no monitored
libraries, and no user has preferences. Each step below is deliberate.

## Turning it on

1. **SMTP** (plugin page -> SMTP server): host, port, encryption (STARTTLS or implicit TLS; credentials are refused
   over unencrypted SMTP), username, sender address and name. Set the password in its own box or mount a Docker
   secret named by `EMAILDIGEST_SMTP_PASSWORD_FILE`. Use **Test connection** and **Send test email** to an address
   you type.
2. **Monitored libraries**: tick the libraries to include and choose each one's category. Nothing is monitored by
   default, and a library is never guessed to be Anime.
3. **Public server address**: the address people use from outside, for example `https://jellyfin.example.org`, with
   the sub-path if the server runs under one. Required to confirm email addresses and to send real digests. Prefer
   `https`; the page warns about `http`.
4. **Digest settings**: default language, first-run lookback, display limits, confirmation link lifetime.
5. Tell users to open their preferences page and confirm an address (`docs/user-guide.md`). The status section shows
   how many have digests on, how many will actually receive one, and how many are waiting for a confirmed address.
6. Enable the digest while **dry run stays on**. Run the task from Dashboard -> Scheduled Tasks and read the status:
   it renders each user's digest and sends nothing.
7. Turn dry run **off** only when you are ready. The scheduled task (daily at 08:00 by default) then sends real mail.

You cannot see or change another user's address or preferences: by design, each person manages their own. To stop
digests for someone, disable their Jellyfin account (they receive nothing and cannot use the page), or stop Jellyfin
and edit `user-preferences.json` (see recovery below).

## Configuration reference

| Setting | Default | Range / rule |
|---|---|---|
| SMTP enabled | off | A live run aborts while off |
| SMTP host / port | empty / 587 | valid host name or IP; 1-65535 |
| Encryption | STARTTLS | STARTTLS, implicit TLS or none (none is refused with a username) |
| SMTP username | empty | one line, 256 characters at most |
| SMTP timeout | 30 s | 5-120 |
| Sender address / name | empty / Jellyfin | valid address; name one line, 100 characters at most |
| Enable digest emails | off | While off the task does nothing |
| Dry run | **on** | On: build and render only |
| Default language | en | en, es, gl: used for users with no language chosen |
| First-run lookback | 168 h | 0-720; 0 means a new user's first digest starts from now |
| Max entries per section / per digest / episodes per series | 25 / 100 / 10 | 1-200 / 1-500 / 1-50 |
| **Public server address** | empty | http or https, host required, no credentials, query or fragment |
| **Confirmation link lifetime** | 60 min | 5-1440 |
| Monitored libraries | none | explicit list with a category each |

An invalid save is refused and the previous settings stay in effect.

## Files in `<server data directory>/EmailDigest/`

In the standard container that is `/config/data/EmailDigest/`. Back up the whole folder; stop Jellyfin (or accept a
tiny risk) before copying. All files are readable by the server account only.

| File | Holds | If lost |
|---|---|---|
| `user-preferences.json` (+ `.bak`, `.v1.bak`) | per-user address, verification, language, on/off | users must set up again; nothing sent meanwhile |
| `delivery-state.json` (+ `.bak`) | per-user checkpoint and delivered items | everyone is treated as new (bounded lookback); duplicates possible |
| `delivery-status.json` | each user's last attempt (display only) | the pages say "nothing yet" |
| `verification-requests.json` | pending confirmations (hashes) and rate-limit log | pending links stop working |
| `unsubscribe.key` | key behind unsubscribe links | **all unsubscribe links in sent emails stop working**; a new key is made |
| `smtp-password.secret` | the SMTP password, unless a Docker secret is used | enter it again |
| `last-run.json` | the last run's counts | the status says "no run yet" |
| `checkpoint.json` | an old global checkpoint, read as a hint only | nothing |
| `storage-migration.json` | marks the copy from the old location as complete | the copy is checked again (harmless) |
| `digest-run.lock` | overlapping-run lock | recreated |

## Recovery

- **Preferences file damaged** (the run stops with "preferences cannot be trusted"; the status says so): stop Jellyfin,
  copy `user-preferences.json.bak` over it (loses only the last change), start. The original format-1 file is kept
  once as `user-preferences.json.v1.bak`. The plugin never overwrites a damaged file.
- **Delivery state damaged**: copy `delivery-state.json.bak` over it (one duplicate digest per user is possible) or
  delete it to treat everyone as new.
- **Verification state damaged**: the plugin moves it aside (`verification-requests.json.damaged-<time>`) and starts
  empty; users ask for a new link.
- **Unsubscribe key wrong size**: runs stop (a digest must carry a working link). Delete `unsubscribe.key` to get a new
  one, accepting that old links stop working.
- **Rotate the unsubscribe key on purpose** (for example after a leak): delete the file; old links stop working.
- **Run says "public address missing"**: set it on the plugin page.

## Upgrade and rollback

State lives outside the plugin folder (`<data>/EmailDigest/`), so installing, replacing or removing a plugin version
never touches it. **Make sure `<data>` is on a persistent volume** (`/config` in the standard container).

*Upgrade from a version that kept state in `plugins/Jellyfin.Plugin.EmailDigest/`*:

1. Back up `<config>` (at least `plugins/` and `data/`).
2. Install the new version in a new versioned folder (`Email Digest_<version>`), remove the old one, restart.
3. On first start the plugin **copies** the old state files into `<data>/EmailDigest/` (checksummed, never overwriting,
   never touching the originals) and writes `storage-migration.json`. If the copy cannot be completed the plugin refuses
   to use any state and logs why; nothing is sent. Fix the cause (permissions, disk) and restart.
4. Existing preferences written by hand or by an earlier version have **no verified address**. Those users receive
   nothing until they confirm an address from their preferences page. Say so before upgrading.

*Rollback to the previous version*: install the old version and restart. It reads the old folder, which still holds
the state as it was at the upgrade (changes made afterwards exist only in `<data>/EmailDigest/`). *Upgrading again*
keeps `<data>/EmailDigest/` and does not import the older copy over it. To discard the new state and redo the copy,
stop Jellyfin and delete the `EmailDigest` folder under the data directory (it is a copy), then start.

Do not install into a folder named exactly `Jellyfin.Plugin.EmailDigest`; use the versioned folder name.

## Reading the status

| Reason code | Meaning | What to do |
|---|---|---|
| `disabled`, `already-running`, `no-libraries` | nothing to do / skipped | none, or tick libraries |
| `configuration-invalid`, `libraries-invalid`, `libraries-unverifiable` | settings or a deleted library | fix the settings |
| `smtp-disabled`, `smtp-password-missing`, `smtp-unusable` | mail login | check SMTP settings and password |
| `public-url-missing` | no public address | set it |
| `unsubscribe-unavailable` | unsubscribe key unusable | see recovery |
| `state-unreadable`, `preferences-unreadable` | a state file cannot be trusted | see recovery |
| `user-failures` | some users could not be delivered to | check the log (counts, no addresses) |

## Logging

Logs and status carry counts, outcome words and exception type names only; no addresses, tokens or titles. Report
problems with those, not with files from the state folder.
