# Phase 7 preflight: findings and design decisions

Written before any Phase 7 code. It records what was checked, against which source, what could
**not** be checked without a running Jellyfin, and the decisions that follow. Base: commit `e87d485`.

Sources are the pinned tags `jellyfin/jellyfin@v10.11.8` and `jellyfin/jellyfin-web@v10.11.8`
(`jellyfin-apiclient@1.11.0`), read from the upstream repositories.

## 1. Existing plugin state (reviewed)

| Item | Finding | Consequence |
|---|---|---|
| `user-preferences.json` | A plain JSON array of `{UserId, Email, Language, NotificationsEnabled}`. No verification state. Nothing in the plugin writes it except `SaveAsync`, which has no caller, so it exists only when seeded by hand. | The schema needs a verified address, a pending address and a version. Hand-seeded records have **no proof of ownership**, so they must be treated as unverified. |
| Repository API | `SaveAsync` replaces a whole record. Two concurrent edits (say language and address) would overwrite each other. A damaged file throws a raw `JsonException`. | Needs an atomic per-user update, and a typed error for a damaged or newer-format file that never overwrites it (as `DeliveryStateStore` already does). |
| `delivery-state.json` | Per-user checkpoint and delivered ids; `.bak` copy; refuses damaged or newer files. Holds no "last accepted" time and records nothing about failed attempts. | A failed attempt must **not** create a record here: a record with no checkpoint would corrupt the new-user baseline. Personal status goes in a separate, purely informational file. |
| All state paths | Every store takes `Plugin.DataFolderPath` = `plugins/Jellyfin.Plugin.EmailDigest/`. | See section 2. |
| File permissions | Only `SmtpPasswordStore` creates owner-only files. `AtomicJsonFile` uses default permissions. | Preferences now hold email addresses and token hashes, so owner-only creation is added. |

## 2. Storage location

`BasePlugin.DataFolderPath` is `plugins/<assembly file name>/`, a sibling of the versioned install
folder (`PluginManager`, v10.11.8). That is outside the version folder but still inside `plugins/`,
which is the tree Jellyfin's plugin manager installs into, renames and cleans. If an installer ever
names the install folder exactly `Jellyfin.Plugin.EmailDigest`, data and code share a folder and
Jellyfin's old-version cleanup could delete the data.

**Decision.** Use `IApplicationPaths.DataPath` (the server's data directory; `/config/data` in the
standard container) and a subfolder `EmailDigest`. It is not under `plugins/`, so no plugin
install, upgrade, disable or uninstall step touches it. Existing files are **copied**, never moved or
deleted, from the old folder on first start (design in the storage commit). The old folder stays as
the rollback copy.

Not verifiable without a server: that `DataPath` is the persistent volume in the user's container.
Check 1 of the live procedure confirms it.

## 3. Identity and authentication (verified in source)

* `CustomAuthenticationHandler` builds the principal from the access token (`Authorization:
  MediaBrowser Token=...`, `X-Emby-Token`, or `api_key`). Claims: `Jellyfin-UserId` (format `N`),
  role `Administrator` or `User`, `Jellyfin-IsApiKey`.
* **An API key authenticates as `Administrator` with an empty user id.** An endpoint that takes
  "the current user" must therefore reject an empty id: an administrator's API key is not a user.
* `[Authorize]` with no policy uses Jellyfin's default policy (custom scheme plus
  `DefaultAuthorizationRequirement`: user exists, remote-access permission, parental schedule).
  `RequiresElevation` is the role claim `Administrator`.
* The browser cannot assert an identity: the only user id the plugin will use comes from the claim.
  A user id in a URL or body is never accepted.
* Jellyfin's own `RequestHelpers.GetUserId` allows an administrator to act as another user. The
  plugin will not offer that: administrators use the administrator page, not user impersonation.
* Not found in the authentication code: any check that a **disabled** user's existing token is
  refused. The plugin therefore checks `IsDisabled` itself on every request.

Not verifiable without a server: the real header names accepted by 10.11.8, the behaviour for a
disabled user's token, and that anonymous requests to `[Authorize]` actions get 401. Live checks 3
to 5.

## 4. Existing administrative boundaries (reviewed)

* `EmailDigestAdminController`: class-level `[Authorize(Policy = RequiresElevation)]`, no
  `AllowAnonymous`, no GET that changes state, `application/json` only on bodies.
* The generic `GET/POST /Plugins/{id}/Configuration` is class-level `RequiresElevation` in
  `PluginsController`. `PluginConfiguration` holds no secret and no per-user data.
* `GET /web/ConfigurationPages` (the list) needs elevation; `GET /web/ConfigurationPage?name=` serves
  the page's HTML with **no authorization**. A plugin page's HTML is therefore public. It must
  contain no data, and the existing administrator page does not.
* Nothing reads or writes `user-preferences.json` over HTTP today.

## 5. How a non-administrator reaches a page

`jellyfin-web` registers the plugin configuration route (`#/configurationpage`) inside
`<ConnectionRequired level='admin' />` (`src/apps/dashboard/routes/routes.tsx`). **A normal user
cannot open a plugin page through the web client**, and plugin pages get no entry in the user menu.

**Decision.** Serve a small standalone page from the plugin's own controller (`GET
EmailDigest/Me/Page`, HTML only, no data, `Cache-Control: no-store`, a strict
Content-Security-Policy, `X-Content-Type-Options: nosniff`, `frame-ancestors 'none'`). The page reads the
signed-in session from the web client's `jellyfin_credentials` entry in `localStorage` (same
origin; key from `jellyfin-apiclient@1.11.0`) and calls the plugin's authenticated API with that
token in a header. Everything shown is written with `textContent`. With no session it says to sign in
and links to the web client. No password is ever typed into the page.

Links reach users through email (the digest footer and the verification message), because there is
no user-menu entry.

Not verifiable without a server: that the key and shape of `jellyfin_credentials` are as read, and
that the page is reachable behind the user's reverse proxy and base URL. Live checks 6 and 7. If the
session cannot be read, the page degrades to the message above and nothing else is lost.

## 6. Links in email, and the public address

Email links need an absolute server address. It must come from administrator configuration
(`PublicBaseUrl`) and **never from the request's `Host` header**, which an attacker controls. Without
it, address verification and live sending are refused with a clear message. The address must be an
`https` URL, or `http` with a warning, and carry no credentials, query or fragment.

## 7. Next scheduled digest

`ITaskManager.ScheduledTasks` exposes each task's `Triggers` (`DailyTrigger`, `WeeklyTrigger`,
`IntervalTrigger`, `StartupTrigger`) and `LastExecutionResult`. A pure calculator will derive the
next time in server local time and say "unknown" otherwise. The interval case is relative to the last
run's end. The page will say a run is scheduled, never that a digest will be sent.

## 8. Decisions on points the specification leaves open

* **Address change.** The current verified address stays in use until the new one is confirmed. A
  pending address is never used. Cancelling or expiry changes nothing.
* **Legacy records.** Hand-seeded preferences have no verification and so stop sending until their
  owner confirms the address. Phase 6 could not send for anyone else, so no real subscriber is
  affected.
* **Disabling.** Takes effect at the next recheck, which now happens immediately before SMTP.
* **Skipped users.** A user who is disabled, unverified or unsubscribed keeps their checkpoint. The
  existing 30-day catch-up cap bounds what they receive when they return; media older than the cap is
  not sent.
* **Confirmation and unsubscribe links** act on `POST`, never `GET`, so link scanners and mail
  prefetchers cannot trigger them. Their tokens carry no readable data.
* **Tokens.** Verification: 256 random bits, stored as a SHA-256 hash, single use, expiry
  configurable (default 60 minutes). Unsubscribe: HMAC-SHA-256 under a server-held random key, so the
  token proves only "the server issued this for this user" and grants no read access.

## 9. Cannot be verified without a running Jellyfin

1. `IApplicationPaths.DataPath` maps to the persistent volume.
2. Anonymous requests to `[Authorize]` actions are refused; a normal user is refused on `Admin`
   routes; an API key is refused on `Me` routes.
3. A disabled user's token is refused (or, if not, the plugin's own check holds).
4. A normal user can open the standalone page and the page can read `jellyfin_credentials`.
5. Behaviour behind the user's reverse proxy and base URL.
6. Mail rendering in real clients.

The unit tests pin attribute declarations and the policy semantics with an in-process pipeline. They
do **not** prove the server's own authentication, which is why the live procedure covers them.
