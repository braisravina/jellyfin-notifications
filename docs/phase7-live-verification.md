# Phase 7 live verification (manual, reversible)

Status: **not yet performed.** Do not describe Phase 7 as live-verified, or send real digests, until this has been
run and the results recorded. The automated tests prove the plugin's logic and the effect of its attributes in
an ASP.NET Core pipeline. They cannot show how **Jellyfin itself** authenticates a token, treats a disabled user,
serves the page to a normal user, lays out its data folders or schedules tasks. Those are what this checks.

## Safety rules

- A **separate, disposable Jellyfin container** with the same custom SSE2-compatible image as production; never
  the production server, never a pulled or replaced image, never a restart of production.
- Its own empty config and cache volumes, a different port, and a few **synthetic** media files mounted read-only.
- No production user, preference, checkpoint, address or SMTP credential is copied or read. The four test users
  below stand in for the four real accounts by *role*; their names, ids and addresses are made up
  (`@example.test`).
- Mail goes only to a throwaway capture server (Mailpit or smtp4dev) on the same Docker network, no relay. No real
  SMTP provider, no real mailbox.
- Remove everything afterwards (end of this document).

## Setup

1. Build (`dotnet build --configuration Release`) and copy `Jellyfin.Plugin.EmailDigest.dll`, `MailKit.dll`,
   `MimeKit.dll` and `BouncyCastle.Cryptography.dll` into `<config>/plugins/Email Digest_0.1.0.0/`.
2. Synthetic media, libraries and the capture server: steps 2, 3 and 5 of `docs/phase6-live-verification.md`
   (libraries Movies A, Movies B, Shows, Anime, Music).
3. The **four synthetic users** (Dashboard -> Users):

   | Name | Libraries | Role in the tests |
   |---|---|---|
   | `t-admin` | all | administrator; also an ordinary subscriber |
   | `t-all` | all | ordinary user with every library |
   | `t-limited` | Movies A only | ordinary user with one library |
   | `t-quiet` | none | ordinary user who never confirms an address |

   Log in to the web client once as each so each has a token; note each user's id (`curl -H 'Authorization:
   MediaBrowser Token="<ADMIN_TOKEN>"' http://HOST:8096/Users`). Create one **API key** (Dashboard -> API Keys).
4. Plugin page: SMTP host/port of the capture server, encryption **None**, sender `digest@example.test`, enable SMTP;
   tick the libraries and categories; set **Public server address** to `http://HOST:8096` (the status will warn
   that it is plain http; that is expected here); leave the digest **off** and dry run **on**.
5. Record the state folder: `<config>/data/EmailDigest/`.

## Checks

For each: do it, write down what happened (pass / fail / surprise).

### A. Storage

1. **Location.** Before the first start of this build, put Phase 6-style files in
   `<config>/plugins/Jellyfin.Plugin.EmailDigest/` (a `user-preferences.json` array with one entry for `t-all`, a
   `delivery-state.json`, a `checkpoint.json`), noting `sha256sum` of each. Start. Expect
   `<config>/data/EmailDigest/` to contain byte-identical copies and `storage-migration.json`; the old files
   unchanged (same checksums). Restart: nothing changes. Confirm `<config>/data` is on the persistent volume.
2. **Owner-only.** `ls -l` the new folder and files: modes `drwx------` / `-rw-------`.
3. **Hand-seeded record is unverified.** With the seeded `t-all` entry (notifications on), run the task live (checks
   below, step 25 first): `t-all` receives nothing until they confirm. The page shows the address as "not
   confirmed".

### B. Administrator page

4. The two new fields save; `ftp://x`, `https://u:p@x` and `https://x?y=1` are refused with a message and the old value
   stays. `http://...` shows the plain-http warning; clearing the field shows the "no public address" warning.
5. The status shows counts (people / digests on / will receive / waiting) and **no address**.

### C. Authentication and authorization (only a live server can show these)

Use `TOKEN_ADMIN`, `TOKEN_ALL`, `TOKEN_LIMITED`, `TOKEN_QUIET` (from the web client's
`localStorage.jellyfin_credentials`, or by `POST /Users/AuthenticateByName`), and `APIKEY`. Header form:
`Authorization: MediaBrowser Token="<token>"`.

6. No token: `GET /EmailDigest/Me` -> **401**. Cookie only -> 401.
7. `TOKEN_ALL`: `GET /EmailDigest/Me` -> 200 with `t-all`'s own overview. Add `?userId=<t-limited id>`: still
   `t-all`'s data. `PUT /EmailDigest/Me/Notifications` with `{"enabled":true,"userId":"<other>"}` changes only `t-all`.
8. `APIKEY`: `GET /EmailDigest/Me` -> **403** (and `Authorization: ... Token="<apikey>"` as `?ApiKey=` too).
9. `TOKEN_ALL` on `GET /EmailDigest/Admin/Settings` and `/Status` -> **403**; `TOKEN_ADMIN` -> 200; no token -> 401.
10. `TOKEN_ADMIN` on `GET /EmailDigest/Me` -> its own overview only; it cannot act for others.
11. `PUT /EmailDigest/Me/Language` with `Content-Type: application/x-www-form-urlencoded` -> **415**.
12. **Disabled user.** Disable `t-quiet` in the dashboard and *keep using its token*. Record whether Jellyfin itself
    rejects the token. Either way `GET /EmailDigest/Me` must return **403** (the plugin's own check). If the token
    still authenticates, note it: the plugin's check is then the only barrier.
13. Pass only `Token="..."` versus a full `MediaBrowser Client=..., Device=..., DeviceId=..., Version=..., Token=...`
    header: both work; the second may rename the device in the dashboard (the page sends only the token for that reason).

### D. The page, as an ordinary user

14. Signed in as `t-all` in a normal browser window, open `http://HOST:8096/EmailDigest/Me/Page`. It shows
    `t-all`'s preferences (not an administrator page). Check the browser console for CSP violations: **none expected**.
    Record whether the session was found (the page reads `localStorage.jellyfin_credentials`; this was derived from
    the source, never run).
15. In a private window (not signed in) the same address shows "Sign in to Jellyfin..." and no data. After sign-out
    in the web client the page does the same. A stale token shows the same message.
16. Behind your reverse proxy / base URL (if you use one) the page, its buttons, the confirm link and the unsubscribe
    link all work under the sub-path; the public address includes the sub-path.
17. Change language in the page to Español, then Galego: all text switches; dates use that language.

### E. Address verification

18. As `t-all`: enter `all@example.test` -> "Confirmation email sent". Mailpit shows **one** message, to that address only,
    in the user's language, with a link under the public address, an expiry time, and **no media, user name, library or
    other address**.
19. Open the link: a page with a button; **opening it (a GET) changes nothing** (the page still says "not confirmed").
    Press the button: confirmed. Press it again (reload the link): "no longer works". The status shows the address as
    confirmed.
20. Request again within a minute: "Try again in N seconds". After waiting, request `x1@example.test`, then
    `x2@example.test`: the first link no longer works, the second does. Request a link and leave it past the lifetime
    (set 5 minutes for this): expired, refused. Cancel a pending request: its link is refused.
21. Ask for the same address from `t-limited` and `t-admin` three times in a day: the plugin refuses further requests
    for that address ("Too many requests") without sending more mail.
22. Change `t-all`'s address while confirmed: until the new link is pressed, `all@example.test` stays in use.

### F. Digests

23. Give each of `t-admin`, `t-all` and `t-limited` a confirmed address (`admin@`, `all@`, `limited@example.test`) and
    language (en, es, gl); turn their notifications on. `t-quiet` turns notifications on and **asks for** an address but
    never presses the link.
24. Enable the digest, keep dry run on, run the task: counts only, **nothing in Mailpit** (apart from the confirmation
    emails already there).
25. Turn dry run off and run. Expect exactly **three** digests: to `admin@`, `all@` and `limited@`, each in its own language,
    `t-limited` seeing only Movies A items, **nothing for `t-quiet`** (or for the seeded unverified record in check 3).
    Each message has a footer with "Manage your notifications" and "Unsubscribe" links and, in the raw source,
    `List-Unsubscribe: <...>` and `List-Unsubscribe-Post: List-Unsubscribe=One-Click`. The three unsubscribe links differ.
26. The page for each user shows "last accepted" (and "accepted by the mail server"), the next run matching the task's
    trigger in Scheduled Tasks, and no other user's data. `t-quiet`'s page shows no accepted digest.
27. Run again with nothing new: no mail. Add media; run: one new digest each.

### G. Unsubscribe

28. Open `t-all`'s unsubscribe link: a page with a button; the GET changes nothing (page still shows digests on).
    Press **Stop the emails**: done. Their page shows digests off; the next run sends them nothing; `t-admin` and
    `t-limited` are unaffected; `t-all`'s Jellyfin account, address and language are intact.
29. One-click: `curl -X POST -d 'List-Unsubscribe=One-Click' '<t-limited unsubscribe link>'` -> 200; `t-limited` is off.
30. A tampered or truncated link (change one character) -> "not valid", nothing changes. Turn `t-all` back on from the page.
31. Delete `unsubscribe.key` (stop Jellyfin first), start, run a live digest: new links appear; an old link is refused.
    (Then continue; this rotates the key.)

### H. Preferences changing during a run

32. To hold a run in the middle, replace the capture server with a tarpit that delays its answer to the first message
    by ~20 s (a few lines of Python `socketserver` that reads until `\r\n.\r\n`, sleeps, then answers `250`). Start a
    live run. While the first user's message is being held, turn the **second** user's notifications off on their page.
    Expect: the first user is sent, the second user is skipped and **not** sent; their checkpoint does not advance
    (run again after turning notifications on: they get what they missed). Repeat with an address change instead of
    a switch-off: the message goes to the new confirmed address or is withheld, never to an unconfirmed one.
33. Disable a user in Jellyfin mid-run: they are skipped.

### I. Failure and recovery

34. Corrupt `user-preferences.json` (`echo '{ x' >`) with Jellyfin stopped; start; run: the run reports "preferences
    cannot be trusted", nothing is sent, the file is unchanged, the pages report a storage problem; restore from `.bak`.
35. Take the capture server down: the user's page shows "last attempt failed"; bring it up, run: delivered, no loss.
36. Upgrade/rollback: install a second plugin version folder, remove the first, restart: `<data>/EmailDigest/` untouched.
    Roll back to a Phase 6 build: it starts from the old folder as it was at step 1. Upgrade again: the new folder
    is kept, the older copy is not re-imported.

### J. Content and logs

37. Search the Jellyfin log directory for every test address and for the first 10 characters of a verification token
    and of an unsubscribe token: **no match**. No `Token=` value in the log.
38. View the digests and confirmation emails in Mailpit's HTML and plain views: layout acceptable; links work.
    (Optional, your decision only: view them in a real mail client using a mailbox you own and a throwaway SMTP
    account; never a production relay.)
39. Spanish and Galician: have a native speaker read the page, confirmation email, footer and administrator strings
    and record corrections.

## Results

Fill in and keep with the commit that records the verification.

| Check | Result | Notes |
|---|---|---|
| 1-3 Storage | | |
| 4-5 Administrator page | | |
| 6-13 Authentication / authorization | | disabled-user token: |
| 14-17 Page | | |
| 18-22 Verification | | |
| 23-27 Digests | | |
| 28-31 Unsubscribe | | |
| 32-33 Changes during a run | | |
| 34-36 Failure / upgrade | | |
| 37-39 Logs / content / language | | |

## Cleanup

Stop and remove the disposable container and its volumes, the capture server and the tarpit script, the synthetic
media, the API key and the test users. Nothing here touched production.
