# Last.fm scrobbling

## What it does

Signed in from Options › Last.fm, Lucent tells Last.fm what's playing ("now playing") and scrobbles each song once it's been heard for half its length or four minutes, whichever comes first, as Last.fm asks. Songs under 30 seconds don't count. Only time spent playing counts (not paused time, not skipping ahead), and a song played again from the start (repeat one) counts again. Scrobbles that can't be sent (offline, Last.fm down) wait in `lastfm-scrobbles.json` in the data folder and go later, up to 50 at a time, for up to two weeks.

| Source | Scrobbled |
| --- | --- |
| Lucent's own music | Always, with album and album artist from the tags. Songs with no artist tag are skipped. |
| Music apps Lucent controls | With the "Songs in music apps" switch (on to start). Spotify and TIDAL can scrobble by themselves, so the hint says to turn it off if the user's already does. |
| YouTube | YouTube Music songs only (they have an album). A plain video's "artist" is its channel, so videos are skipped. |
| Online radio | With the "Online radio" switch (on to start), when the station names the song as "Artist - Title". The length isn't known, so it counts after a minute heard, as not chosen by the user. Show names (live DJ stations) are skipped. |

Sign-in uses Last.fm's desktop flow: the browser opens Last.fm's page, the user allows Lucent, and Lucent asks every 3 s (for up to 5 minutes) for the session. The session never expires; if Last.fm takes it back (error 9), Lucent signs out and says so on the tab. Signing out forgets waiting scrobbles. "My Last.fm" opens the user's profile.

## Windows

Session key in `%AppData%\Lucent\lastfm-sign-in.dat`, encrypted with DPAPI. The web-browser item covers every site (Windows doesn't say which), so the YouTube rule (album needed) applies to all of them: SoundCloud in a browser is scrobbled only when it gives an album.

## Mac

Session key in the login keychain ("Lucent Last.fm sign-in"). SoundCloud tabs are scrobbled.

## Parity gaps

None beyond the browser note above, which comes from Windows' media controls.

## Code

- `Lucent.Core/Lastfm/` (`Scrobbler`: the rules and the waiting list; `LastfmClient`: sign-in, signing, now playing, scrobbles; `ScrobbleSelfTest`)
- `Lastfm/LastfmAccount.cs` in each app
- `MainWindow.Lastfm.cs` in each app (what's playing, the Last.fm tab); the player's position timer calls `ObserveForScrobbling` five times a second.
- The app's Last.fm API key and shared secret come from the `LASTFM_API_KEY` and `LASTFM_SHARED_SECRET` repository secrets (build properties `LastfmApiKey`, `LastfmSharedSecret`); `LUCENT_LASTFM_API_KEY` and `LUCENT_LASTFM_SHARED_SECRET` override them. A build without them shows the tab with sign-in turned off.

## Checks

- `ScrobbleSelfTest` in both self-tests: half or four minutes, under 30 s, pausing, repeat, radio, no artist, offline batching, sign-in taken back, and the call signature.
- Mac screenshot `Player-Options-Lastfm`.

## Not verified

Not yet tried against the live Last.fm API (the cloud build machine can't reach it); first sign-in on a real Mac and PC should be checked.
