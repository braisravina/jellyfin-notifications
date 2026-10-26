# Phase 6 live verification (manual, reversible)

Status: **not yet performed.** Do not describe Phase 6 as live-verified until this has been run and the
results recorded. The unit tests prove the logic with fakes; they cannot prove how Jellyfin discovers
media, enforces library permissions, schedules tasks or lays out the plugin folders.

## Safety rules

- Use a **separate, disposable Jellyfin container**, never the production one. Use the same custom
  SSE2-compatible image the production host runs; do not pull, replace or upgrade any image.
- Own empty config and cache volumes, a different port, and a tiny **synthetic** media folder mounted
  read-only. No production users, preferences, checkpoints or SMTP credentials.
- Mail goes only to a throwaway capture server (Mailpit or smtp4dev) on the same Docker network, with no
  relay or forwarding. No real SMTP provider, no real addresses.
- Do not install into, or restart, the production server.

## Setup

1. Build: `dotnet build --configuration Release`. Copy `Jellyfin.Plugin.EmailDigest.dll`, `MailKit.dll`,
   `MimeKit.dll` and `BouncyCastle.Cryptography.dll` into `<config>/plugins/Email Digest_0.1.0.0/`.
2. Synthetic media (any tiny valid files work; with ffmpeg):

   ```shell
   mkdir -p media/{MoviesA,MoviesB,Shows,Anime,Music}
   ffmpeg -f lavfi -i color=c=black:s=64x64:d=1 -y "media/MoviesA/Film One (2020).mkv"
   ffmpeg -f lavfi -i color=c=black:s=64x64:d=1 -y "media/MoviesB/Hidden Film (2021).mkv"
   ffmpeg -f lavfi -i color=c=black:s=64x64:d=1 -y "media/Shows/Some Show/Season 01/Some Show S01E01.mkv"
   ffmpeg -f lavfi -i color=c=black:s=64x64:d=1 -y "media/Anime/Some Anime/Season 01/Some Anime S01E01.mkv"
   ffmpeg -f lavfi -i sine=frequency=440:duration=1 -y "media/Music/Artist/Album/01 - Track.mp3"
   ```

3. In the disposable Jellyfin create libraries: Movies A, Movies B, Shows, Anime (TV type), Music.
4. Create users: `admin`, `all` (all libraries), `limited` (Movies A only), `none` (no libraries), `paused`
   (all libraries).
5. Start the capture server; note its SMTP host name and port on the Docker network.
6. On the plugin page: tick all five libraries and choose categories (Anime library -> **Anime**, Shows ->
   Series); set SMTP host/port, Encryption **None** (capture server), sender `digest@example.test`; save;
   enable SMTP delivery. Leave "Enable digest emails" and "Dry run" for the scenarios below.
7. Seed preferences. Get user ids (`curl -H 'Authorization: MediaBrowser Token="ADMIN_TOKEN"'
   http://HOST:8096/Users`), then create `user-preferences.json` in the state folder (see check 1) as a
   JSON array with one object per user:

   ```json
   [
     { "UserId": "<all-guid>", "Email": "all@example.test", "Language": "en", "NotificationsEnabled": true },
     { "UserId": "<limited-guid>", "Email": "limited@example.test", "Language": "es", "NotificationsEnabled": true },
     { "UserId": "<none-guid>", "Email": "none@example.test", "Language": "gl", "NotificationsEnabled": true },
     { "UserId": "<paused-guid>", "Email": "paused@example.test", "Language": "en", "NotificationsEnabled": false }
   ]
   ```

## Checks

1. **Data folder location.** The state folder is `<config>/data/EmailDigest/` (the server's data directory), not
   under `plugins/`. Record the real path and confirm it is on the persistent volume; this decides whether state
   survives upgrades. (Earlier versions used `plugins/Jellyfin.Plugin.EmailDigest/`; see the README.)
2. **Disabled does nothing.** With the digest disabled run the task from Dashboard -> Scheduled Tasks. The task
   succeeds, the log says it is disabled, the capture server is empty, no `delivery-state.json` appears.
3. **Dry run.** Enable the digest, keep dry run on, run the task. Status shows mode "Dry run" and a last run with
   digests rendered for `all`, `limited`, `none`; the capture server is still empty; no `delivery-state.json`.
4. **Live, first run.** Turn dry run off, run the task. The capture server shows one separate mail each for the
   users who can see new media, in the user's language (`limited` in Spanish). `limited` must **not** list Movies B
   or any library it cannot see; `none` gets nothing; `paused` gets nothing. Every mail shows only its own
   recipient. Anime episodes appear under Anime, not Series. Status shows the counts; `delivery-state.json` has
   one record per delivered user.
5. **No duplicates.** Run the task again straight away: no new mail (counts show "no new media").
6. **Media added later.** Add a new film to Movies A and scan the library. After the 5-minute settle margin run
   the task: exactly one new mail with only that film, to the users who can see it.
7. **Partial failure is visible.** Make one address fail (stop the capture server for a moment, or point the
   server at a closed port). The task shows **Failed** in the task list, Status shows "Completed with failures"
   or "Stopped early" with a reason, and `delivery-state.json` is unchanged for the failed users. Restore the
   server and run again: the missed mail arrives once.
8. **Login failure stops the run.** Use a capture server that requires a login with a wrong password (or set a
   username with the wrong password over TLS): the run stops after the first user, Status says the mail
   server refused the login, and no checkpoint moved.
9. **Overlap.** Press Run twice quickly. Only one run happens; the second logs "already in progress".
10. **Cancel.** Start a run with several users and cancel it from the task list: the task ends cancelled; users
    already delivered keep their new checkpoint, the rest are untouched.
11. **Crash recovery.** `kill -9` the Jellyfin process during a run, start it again and run the task: no stuck
    lock. A user whose mail was accepted but not yet recorded may receive one duplicate (documented).
12. **Deleted library.** Delete the Movies B library in Jellyfin and run the task: it **aborts** (reason: a
    monitored library no longer exists), sends nothing and moves no checkpoint. Re-save the monitored libraries
    without it and the next run proceeds.
13. **Limits carry over.** Set "Most items per category" to 2 and add 6 films to Movies A. The first run mails
    2 films plus the "rest follows in your next digest" line; running again straight away mails the next films;
    after three runs all six have been delivered once and a fourth run sends nothing.
14. **Damaged state.** Stop Jellyfin, corrupt `delivery-state.json`, start it and run: the run aborts, sends
    nothing, and the file is untouched. Restore `delivery-state.json.bak` and run again.
15. **Paused user.** Turn notifications on for `paused` in `user-preferences.json` and run: they receive the
    media added since their checkpoint (or the bounded lookback if they have none), not the whole library.
16. **Upgrade keeps state.** Copy the plugin to a folder with a higher version name (for example
    `Email Digest_0.1.1.0`, assembly version bumped) and remove the old folder, restart: `delivery-state.json`,
    `user-preferences.json` and the SMTP password are still there and nothing is resent.
17. **Logs.** Search the Jellyfin log for `Email Digest`: lines show counts and phases only. No addresses, names,
    titles, passwords or message text.

## Record

Jellyfin version, plugin version, image tag, capture server, the real data-folder path, and the result of each
numbered check, with any difference from this document.

## Rollback

Remove the disposable Jellyfin and capture containers and their volumes. Nothing in production was modified.
