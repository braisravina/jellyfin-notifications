# Phase 5 live verification (manual, reversible)

Status: **not yet performed.** Do not describe Phase 5 as live-verified until this has been run and
the results recorded. The unit tests prove the logic against a fake SMTP server on loopback; they
cannot prove that Jellyfin enforces the administrator policy, that the dashboard page works, or
that a real mail provider accepts the message.

## Safety rules

- Use a **separate, disposable Jellyfin container**, never the production one.
- Use the same custom SSE2-compatible image the production host runs. Do not pull, replace or upgrade
  any image.
- Give it its own empty config/cache volumes and a different port. Do not copy production users,
  preferences, checkpoints or SMTP credentials into it.
- Do not install the plugin into, or restart, the production server.
- Use a throwaway SMTP capture service for the first parts (below), and a dedicated, send-only test
  account for the TLS part. Never use a personal or production mail account.
- No digest is sent by this version, so nothing here can mail your users. The only mail sent is the
  test message you request.

## Part A: plugin loads with its mail library

1. `dotnet build --configuration Release`.
2. Stop the disposable container, create `<config>/plugins/Email Digest_0.1.0.0/` and copy into it
   `Jellyfin.Plugin.EmailDigest.dll`, `MailKit.dll`, `MimeKit.dll` and `BouncyCastle.Cryptography.dll`.
3. Start it. Dashboard -> Plugins: "Email Digest" is listed and is not `NotSupported`.
4. The Jellyfin log shows no assembly-load or dependency-injection errors mentioning `MailKit`,
   `MimeKit`, `BouncyCastle` or `Email Digest`.
5. Open the plugin page: all four sections render with text in your browser language (try English,
   Spanish and Galician browser settings).

## Part B: capture server, unencrypted (no secrets involved)

Run a disposable capture server on the same Docker network, for example smtp4dev or Mailpit, with
no relay or forwarding configured. Then on the plugin page:

1. Host = the capture container's name, port = its SMTP port, Encryption = **None**. The red warning
   appears and the username field is disabled and empty. Sender = `digest@example.test`. Save.
2. Test connection: succeeds, and the capture server shows **no** message.
3. Send test email to `you@example.test`: succeeds; the capture server shows exactly one message,
   multipart with a plain-text part first and an HTML part second, in your language, with only that
   one recipient. A second press within five seconds is refused.
4. Send test email to `not-an-address`: the page refuses it and nothing arrives.

## Part C: TLS behaviour

With a capture or test server that offers TLS using a certificate from a CA the container does **not**
trust (for example the tool's self-signed one):

1. Encryption = STARTTLS or implicit TLS: both tests fail with the TLS message, and no message
   arrives. There must be no way in the page to accept the certificate.
2. Encryption = STARTTLS against a port that does not offer STARTTLS: fails with the TLS message and
   nothing is sent in plain text.
3. Install the test CA in the container's trust store (or use a certificate from a public CA with a
   dedicated test account): the test now succeeds.

## Part D: password handling

1. Set a distinctive test password such as `LiveCheck-7f3a`. Open browser developer tools -> Network
   before doing so. Confirm the password appears only in the **request body** of `PUT .../Password`,
   never in a URL, and in no response.
2. Reload the page. The password field is empty and the status says a password is set.
3. Inspect every response of the page (`Settings`, `Strings`, the generic
   `GET /Plugins/0e2dad96-d957-4026-8abb-0ca78068c5f0/Configuration`) and view the page source and
   the browser's local/session storage: the password appears nowhere.
4. `grep -r LiveCheck-7f3a` the container's Jellyfin log directory: no match. Note that
   `<config>/data/EmailDigest/smtp-password.secret` **does** contain it, as plain text;
   confirm its mode is `-rw-------` (`ls -l`). This is the documented limitation.
5. Remove password: status says none; `smtp-password.secret` is gone.
6. Docker secret: stop the container, add `EMAILDIGEST_SMTP_PASSWORD_FILE` pointing at a mounted
   secret file, start it. The page says the password is provided by the deployment and the input and
   buttons are disabled. `PUT`/`DELETE Password` return 409.
7. Authentication against a server that requires a login (a dedicated test account, over TLS): wrong
   password -> "rejected the username or password"; right password -> accepted.

## Part E: authorization (needs two Jellyfin accounts and, optionally, an API key)

Replace `HOST`, `TOKEN` and the plugin routes as appropriate. `TOKEN` is an access token or API key.

```shell
# unauthenticated: expect 401
curl -i http://HOST:8096/EmailDigest/Admin/Settings

# non-administrator token: expect 403
curl -i -H 'Authorization: MediaBrowser Token="NON_ADMIN_TOKEN"' http://HOST:8096/EmailDigest/Admin/Settings

# administrator token: expect 200 and no password anywhere in the body
curl -s -H 'Authorization: MediaBrowser Token="ADMIN_TOKEN"' http://HOST:8096/EmailDigest/Admin/Settings
```

Repeat the unauthenticated and non-administrator calls for **every** route: `PUT Settings`,
`PUT Password`, `DELETE Password`, `POST TestConnection`, `POST TestEmail`, `GET Strings`. All must
be refused. Also confirm a `PUT Password` with `Content-Type: text/plain` is refused (415) and that a
password in the URL (`?password=x`) is ignored.

## Part F: failure reporting

Check each outcome shows a clear, localized message and no server text: wrong host (connection
failed), wrong port, firewall-blocked port (timeout), TLS mismatch, wrong password, sender or
recipient refused by the server. Confirm the Jellyfin log for each contains no password, username,
recipient address or message text.

## Record

Jellyfin version, plugin version, image tag, mail server used, and the result of each numbered step.

## Rollback

Remove the disposable Jellyfin and capture containers and their volumes. Nothing in production was
modified.
