# Mica for iOS

Status (2026-10-26): a first version is written, but it hasn't been compiled yet, because there's no Mac here.
It will be built by GitHub Actions on a macOS runner (`.github/workflows/ios.yml` at the repository root). That workflow builds for the
simulator, takes screenshots in English, in Vietnamese and in dark mode, and uploads them with the build log.

It's a port of Mica for Android (`android\`), which is itself a port of desktop. It uses UIKit with no storyboards,
the system WKWebView, iOS 17 or later, and has no third-party code. Like the Android port, it covers every desktop
feature except the AI panel and DevTools. The standing goal is the same: as little memory, CPU and battery as
possible, smooth, and never cut off media that's playing.

## Building

On Windows, before pushing (and again whenever the shared files change):

```bash
node ios/tools/prepare.js
```

That script copies into `Mica/Resources` everything iOS shares with desktop and Android:

- the 12 game pages, `Reader.js`, the login, TOTP and passkey scripts, and `psl.dat`, into `Shared/`;
- the blocker lists, converted to one WebKit content-blocker list in `Blocker/rules.json`, with its counts in
  `Blocker/meta.json`;
- the strings, from Android's `strings.xml`, the generated Vietnamese file and `strings-ios.json`, written to
  `en.lproj` and `vi.lproj`.

On a Mac or the CI runner:

```bash
brew install xcodegen
xcodegen generate
xcodebuild -project Mica.xcodeproj -scheme Mica -sdk iphonesimulator -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO build
```

The bundle id is `online.lessnoise.mica`. To install on a phone or upload to the App Store, you need an Apple
Developer account: set `DEVELOPMENT_TEAM` in `project.yml`, then sign and archive.

## How it maps from Android

| Android | iOS |
|---|---|
| WebView + `androidx.webkit` profiles | `WKWebView`. Cookie jars are `WKWebsiteDataStore(forIdentifier:)` (the jar id is a UUID); private tabs use `.nonPersistent()` |
| Native blocker matching in `shouldInterceptRequest` | A compiled `WKContentRuleList` (135,915 WebKit rules from the same lists, compiled once per build). Sites where ads are allowed get pages without the list. WebKit blocks inside its own process, so iOS can't count blocked requests |
| `games.mica.invalid`, served from assets | `mica://` served by a `WKURLSchemeHandler`. The pages' `https://*.mica.invalid` links are mapped back to `mica://` |
| Error page injected after load | `loadHTMLString` with the failing URL as the base URL |
| `@JavascriptInterface` | `webkit.messageHandlers` (`micaMedia`, `micaReader`, `micaLogin`). A small shim gives the shared scripts their `window.micaLogin.postMessage` |
| Resting tabs (`saveState`) | `interactionState` |
| Keystore (`Sealed`) | Keychain, this device only |
| `MediaService` | The `playback` audio session and the `audio` background mode |
| Activities (settings, passwords) | Pushed and presented view controllers |

The vault, TOTP, CSV import and the sync service are byte-for-byte the same as desktop and Android (PBKDF2-SHA256
with 600,000 rounds, AES-256-CBC with HMAC-SHA256, and desktop's JSON). So a vault from Windows or Android opens
here with the same master password, and syncs back.

## Files

| File | What it is |
|---|---|
| `App.swift` | App and scene delegates: links from other apps, background and foreground |
| `MainViewController.swift` | The one screen: page, island, the page menu, the site card, tab, group and jar menus, sessions, send to device, and the sign-in code card |
| `Browser.swift` | Tabs, spaces, groups, jars, split, history and the library lists; web view setup and the navigation and UI delegates |
| `Island.swift`, `Spotlight.swift`, `Rail.swift`, `Deck.swift`, `HomeView.swift`, `Peek.swift`, `SplitView.swift`, `Sheet.swift` | Mica's UI pieces, as on Android |
| `Library.swift` | Bookmarks, history, read later, recently closed, and downloads (QuickLook) |
| `Settings.swift` | Settings, allowed sites, site permissions and clear data |
| `Passwords.swift`, `Autofill.swift`, `Account/` | Account, vault, sync, autofill and 2FA codes |
| `Navigation.swift`, `Reader.swift`, `SchemeHandler.swift`, `Blocker.swift`, `Model.swift`, `Theme.swift`, `Sessions.swift` | Addresses and search, reading mode and the error page, `mica://`, the blocker, saved state and preferences, and the palette |

## Not on iOS (yet)

- **Blocked-request counts.** WebKit doesn't report them.
- **Highlights.** There's no text-selection menu hook yet.
- **Being the default browser.** That needs Apple's `com.apple.developer.web-browser` entitlement, which you apply
  for with a developer account.
- **Language picker.** iOS keeps each app's language in the Settings app, so Mica's language row opens Settings.
- **Passkeys.** These go through the system (as they do on Android).
