# Email digest: security considerations

What the plugin protects, how, and what it does not. Read together with `docs/phase7-preflight.md` (the identity
and authorization facts, verified against the Jellyfin 10.11.8 sources) and `docs/phase7-live-verification.md`
(what only a running server can show).

## Assets and who may touch them

| Asset | Held in | Who can read or change it |
|---|---|---|
| SMTP credentials | Docker secret file, or `smtp-password.secret` (owner-only, plain text) | Administrators set it, nobody reads it back; the server account reads it to send |
| Users' addresses and preferences | `user-preferences.json` (owner-only) | Each user sees and changes only their own; administrators see counts only |
| Verification tokens | `verification-requests.json`: only SHA-256 hashes | Nobody; the raw token exists only in the email |
| Unsubscribe key | `unsubscribe.key` (owner-only, 256 bits) | Server account only |
| Delivery state | `delivery-state.json`, `delivery-status.json` | The runner; a user sees only their own last-accepted/attempt/next-run |
| Global settings | Jellyfin plugin configuration | Administrators only |

All state lives in `<server data directory>/EmailDigest/`, created readable by the server account only.

## Identity and authorization

- User endpoints (`EmailDigest/Me/...`) require a signed-in Jellyfin user. The user acted on is read from the
  authenticated claims; no route, query, header or body field can name a user, so one user cannot act on another.
- An administrator is an ordinary user here: their own notifications only. An **API key is refused** (it
  authenticates as an administrator without a user id, so it is not a person). A disabled or removed user is
  refused by the plugin itself, because the Jellyfin sources do not show their existing tokens being rejected.
- Administrator endpoints (`EmailDigest/Admin/...`) require Jellyfin's `RequiresElevation` policy; the generic
  plugin configuration endpoints are administrator-only in Jellyfin itself. SMTP settings, the password,
  monitored libraries and the public address are therefore unreachable to ordinary users.
- Anonymous surface, in full: the four page GETs (static HTML and interface text, no data) and two POSTs that
  take a token from the query (`Verify`, `Unsubscribe`). A structural test pins this list.
- **What the tests prove and what they do not.** The automated tests send real requests through the real ASP.NET
  Core pipeline with claims shaped like Jellyfin's and the policies Jellyfin defines. They cannot prove that
  Jellyfin's own authentication produces those claims, rejects a disabled user's token or lets an ordinary user
  through its default policy. Those are checked in the live procedure. Do not rely on them until you have.

## Verification of an address

- Tokens are 256 random bits, shown once, stored hashed, single use, expiring (5 to 1440 minutes, default 60) and
  replaced by any newer request. Unknown, used, expired, replaced and cancelled links look identical.
- Redeeming uses the token up *before* recording the address, so a failure cannot make a link reusable, and two
  simultaneous clicks cannot both succeed.
- Per user: one request a minute, three an hour, ten a day. Per address (any user): three a day. Overall: sixty an
  hour. These are persisted and survive a restart. They bound how much mail one user can make the plugin send to
  a stranger, and what the plugin can ask the SMTP server to send.
- The confirmation email names no user, library or media, only the server's display name, so it is safe in a
  stranger's inbox. **Residual risk:** a signed-in user can still cause up to the limits above of such emails to an
  address they do not own. The person who owns it can ignore them; there is no address-wide block list.
- Confirming does not require being signed in, because the recipient may open the mail on another device. The
  token is the proof; it can only confirm that one pending address for the user who asked.
- Until confirmed, a pending address is never used. A record written by hand or by an earlier version is treated
  as unverified and receives nothing.

## Links in email

- Every link is built from the administrator-configured **public server address**, never from a request's `Host`
  or forwarded headers (which a caller controls). Without it, verification and real digests are refused.
- A link only ever shows a page; the change happens when a button is pressed (POST), so mail scanners and link
  previews that fetch links change nothing.
- Unsubscribe tokens are HMAC-SHA-256 of the user id under the server's key. They carry no address or name, cannot
  be forged, and the only thing they can do is turn that user's notifications off. They do not expire, so old
  emails keep working; deleting `unsubscribe.key` invalidates all of them.
- With an `http` public address the one-time tokens travel unencrypted; the settings page warns about it.
- `List-Unsubscribe` and `List-Unsubscribe-Post` (RFC 8058 one-click) headers are added. Some large mailbox
  providers also expect the message to be DKIM-signed by the sending domain; that is the mail server's job.

## The pages

Served from the plugin because Jellyfin keeps plugin pages behind an administrator check. They are public static
HTML with a Content-Security-Policy that allows only the page's own script and style (by hash), no framing, no
forms, connections only to the same server, no caching and no referrer. Text is inserted with `textContent`; the
script has no `innerHTML`, eval or inline handlers. The page reads the web client's own session from
`localStorage` (key `jellyfin_credentials`), uses a token only if it belongs to this server, and sends only the
token (anything else would rename the browser's session in Jellyfin's device list). It never asks for a password.

CSRF: tokens travel in a request header, not a cookie, so another site cannot make a browser send an authenticated
request; changes also require `application/json`, which a cross-site form cannot send. The two anonymous POSTs
need a secret token the attacker does not have.

## Digest content and permissions

Discovery runs as each user with that user's own library permissions; a plugin-side check covers the case where
Jellyfin skips its per-user filter. Media metadata (titles, names) is treated as untrusted and escaped in both
parts of the email. A user's digest is built and sent separately; there are no shared recipient lists and no Cc or
Bcc.

## Sending rules

- Sent only to a verified address of a user who wants digests, whose Jellyfin account is enabled.
- Rechecked **immediately before every SMTP attempt**, including retries: a subscription turned off, an address no
  longer verified or an account disabled stops the send and leaves that user's checkpoint alone. A change made in
  the milliseconds between the last check and SMTP accepting the message can still produce one email.
- Delivery is at-least-once (a lost SMTP reply can lead to one duplicate). "Accepted" never claims inbox delivery.

## Logging and privacy

Logs and the status page carry counts, outcome words and exception type names only: no addresses, tokens, names,
titles or user ids. A user's page shows only their own information and never an internal path, a raw error or
another user's address.

## Known limits and risks

- Not exercised on a live server: Jellyfin's authentication behaviour, the page inside the real web client and
  behind your reverse proxy, mail rendering in real clients, Spanish and Galician wording (needs a native speaker).
- The SMTP password file is plain text (owner-only); prefer the Docker secret.
- A server-side attacker with file access can read addresses; the plugin does not encrypt its state.
- Verification emails to an address the requester does not own are bounded, not impossible (see above).
- Rate limits for ordinary changes are in memory and reset on restart; the limits on verification are persisted.
- No account recovery: if a user loses access to their address, they confirm a new one.
