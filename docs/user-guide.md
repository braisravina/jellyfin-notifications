# Email digest: guide for users

The server can email you a short list of what has been added since your last digest. This page explains how to
turn it on, where it is sent, and how to stop it. Nothing is sent to you until you have confirmed an email
address.

## Open your preferences

Open **your preferences page** from the link at the bottom of any digest email ("Manage your notifications"), or
type the address of your server followed by `/EmailDigest/Me/Page`, for example
`https://jellyfin.example.org/EmailDigest/Me/Page`.

You must be signed in to Jellyfin **in the same browser**. If you are not, the page says so and offers a link
back to Jellyfin; sign in there and open the page again. The page never asks for your password.

## Turn the digest on

1. Under **Email address**, type your address and press **Send confirmation email**.
2. Open the email and press the link. A page asks you to press **Confirm this address**. This proves the address
   is yours; until then nothing is sent to it.
3. Back on your preferences, tick **Send me email digests of new media**.
4. Choose the **language** of your digests and emails (English, Español or Galego).

The confirmation link works **once** and stops working after the time your administrator chose (an hour unless
they changed it). If it has expired or you asked for a newer one, ask for a new link on your preferences page.
If you did not ask for a confirmation email, ignore it: nothing is sent to that address and nothing changes.

You can ask for a link a limited number of times (one a minute, three an hour, ten a day); the page tells you
how long to wait.

## Change your address

Type the new address and press **Send confirmation email**. Until you press the link, digests keep going to
the address you already confirmed. When you confirm the new one, it replaces the old. **Cancel this request**
withdraws a request you changed your mind about.

## What you receive

A digest lists the new movies, series, music and so on **that your own Jellyfin account may see**. Someone with
access to fewer libraries gets a shorter list. If nothing is new for you, no email is sent. Every digest ends with a
link to your preferences and a link to **unsubscribe**.

## Stop the emails

- Untick **Send me email digests of new media** on your preferences page, or
- press the **Unsubscribe** link at the bottom of any digest, then **Stop the emails**. You do not need to be
  signed in to do that. Most mail programs also show an "Unsubscribe" button for these emails.

Stopping emails does **not** delete your Jellyfin account or anything else. You can turn them back on any
time from your preferences page. If you were away while emails were off, the next digest covers what was added
in the last 30 days at most.

## What the status section means

- **Last digest accepted by the mail server**: when the mail server last took a digest for you. That does not
  promise it reached your inbox; check your spam folder.
- **Last attempt**: whether the most recent try was accepted, failed (it will be tried again at the next run) or
  uncertain (it may or may not have been sent).
- **Next scheduled run**: when the server will next look for something new. A run sends an email only if there
  is something new for you.
- If the page says the administrator has not switched on sending, no digests are sent yet.

## What is stored about you

Your Jellyfin user id, your confirmed address (and a pending one while you confirm it), your language, whether
digests are on, when the last digest was accepted, and a note of which items were already included so they are
not repeated. No password and no viewing history. Administrators can see only counts (how many people have
digests on), not addresses.
