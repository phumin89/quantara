# Protected Bitkub wallet viewer

This milestone displays an authenticated wallet observation. It does not provision an execution account, establish a stable Bitkub account ID, reconcile orders/fills, approve limits or activate trading. The executor remains DISABLED because no trading/account-store services are registered. Read-only wallet data never funds a Sandbox ledger or authorizes an order.

## Start

`tools/start-live-readonly.ps1` prepares and starts the Docker owner viewer. The project `.env` already contains the owner-entered `BITKUB_API_KEY` and `BITKUB_API_SECRET`; do not paste them into chat. It is mounted read-only only in the owner container, never in Node. The same bounded literal parser serves the wallet checker and host. Credentials are not exported to model-process environment or browser code.

Double-click `Quantara.cmd`, or run the script directly. Startup reuses the verified owned installation and opens the same HTTPS origin, without rotating its certificate or issuing a code when remembered state exists. Ports 3000/7443 must be free or belong to that owned service. First setup requires an existing trusted localhost development certificate and exports its encrypted PFX/configuration into ignored `.runtime/owner-tls`. Normal startup preserves these files; `-Rebuild` explicitly rebuilds source. No Windows code-integrity policy or certificate-validation protection is changed.

The protected host runs through Docker because Windows Application Control currently blocks native host assemblies. The launcher starts only the owner service and internal frontend. As requested on 2026-10-05, there is no Sandbox UI or mode switch, and starting Live does not start scheduled Sandbox AI work, its API, report database or Pixel office. Historical paper data and volumes remain intact for opt-in headless verification. No Windows security policy is changed.

Host ports bind only to 127.0.0.1; the internal frontend has no published port. The owner host terminates HTTPS and forwards public GET/HEAD requests to Node without cookies, Authorization or broker headers. Fixed owner origin/authority, Secure/HttpOnly/SameSite=Strict cookies and CSRF controls remain enforced. Other methods do not reach Node.

The owner service runs detached in Docker with `restart: unless-stopped`; closing the pairing window does not stop the website. A separate Windows Terminal window titled `Quantara - Bitkub pairing` shows a one-time pairing code. If Windows Terminal is unavailable, the launcher uses a visible Command Prompt window. The interactive client uses an OS-local, same-user named pipe inside the owner container; there is no HTTP code-generation route, and neither service logs nor redirected client output expose a code. Open `https://localhost:3000` and paste the code into the form shown above the collapsed readiness details. Close the pairing window after use. No code should be sent in chat.

The explicit **remember this browser for viewing for 30 days** checkbox is selected by default. Remembered viewing uses an absolute 30-day persistent cookie, with no sliding renewal. It survives closing the browser and restarting/recreating the owner service with the same certificate and `owner-state` volume. Keys are encrypted with the existing HTTPS certificate using ASP.NET Core Data Protection; the revocable viewing record is also protected and stores only a session hash, not its token. Neither the state volume nor certificate is mounted in Node/Sandbox. Missing or corrupt protected state grants no access. Without consent, the existing nonpersistent eight-hour session remains and does not survive restart. Cookies issued before this update require one new pairing.

Read routes use the viewing policy. Financial mutation routes retain a separate process-local owner-control policy that expires after eight hours and is always lost on restart, even if viewing is remembered. Remembering never arms an account, changes limits or registers execution services. The logout action works after control access has expired, deletes the browser cookie and revokes the saved viewing record so copied cookies cannot work after restart.

For an expired code, `tools/pair-live-owner.ps1` opens a fresh pairing client without rebuilding or restarting Sandbox/the website. It verifies the managed owner container's running state and exact project/service labels. A new code revokes the previous owner viewing session.

## Observation behavior

The app now has persistent Office, Account/assets and Trading routes. The owner proxy forwards only bounded public Next navigation headers and safe public relative redirects so client navigation works, while continuing to strip owner cookies, Authorization, CSRF and upstream Set-Cookie. Both Docker services restart unless explicitly stopped. Pixel assets are bundled; no separate Pixel server or paper worker starts with Live. Warm opening keeps the existing certificate and protected viewing state. Pairing/restart controls and the financial release gates below remain unchanged.

Owner-only `/live/wallet` reuses signed Bitkub V4 wallet reads. Reads are serialized, limited to ten seconds and cached for under twenty seconds for the same credential reference. Credential changes, malformed files, invalid amounts, duplicate currencies, missing THB, stale cache or backwards clock never return an earlier observation as a new successful read. UI data expires at thirty seconds and is cleared on auth failure/actions. Zero THB is a valid observed value; unavailable data is not zero.

Source/time, THB available/reserved/total and nonzero coin quantities are shown. No market valuation or execution readiness is inferred. Account identity verification remains false, and key hashing is only an opaque credential reference, not a Bitkub user ID. No broker call happens until an authenticated owner asks for the wallet. No placement, cancellation, withdrawal or deposit call is made by this viewer.

## Remaining trading work

The current wallet is not persistent trading-account binding. Production normalization of quantities/fees/credits and complete order evidence, account provisioning/reconciliation, daily baseline lifecycle, positive owner limits and explicit activation remain release gates. The owner's 700/2100-THB example was illustrative and is not configured as a target. Jev remains deferred.

Primary references: [Bitkub REST V4](https://github.com/bitkub/bitkub-official-api-docs/blob/master/rest-v4.md), [REST V3](https://github.com/bitkub/bitkub-official-api-docs/blob/master/rest-v3.md), [Microsoft certificate tooling](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs).
