# Mica browser

Approved direction: Windows desktop browser inspired by Arc, with Spotlight search.

Native WPF chrome uses muted lavender, charcoal text and a warm ivory start page. Sidebar holds spaces, pinned tabs and regular tabs. A centered Spotlight overlay searches across all spaces, bookmarks and history, then offers web navigation/search. Ctrl+K opens Spotlight, Ctrl+L edits the current address, Ctrl+T opens a new-tab Spotlight. Arrow keys, Enter and Escape work without a mouse.

WebView2 supplies real web navigation. Each live tab owns one WebView2 control with a shared environment. Background tabs are discarded after two idle minutes unless playing audio; a manual sleep command is available. Discarded tabs keep only URL/title/space metadata and reload on activation. Unsubmitted page state may be lost when discarded. No RAM-per-tab guarantee; report host plus WebView2 process working sets and label the metric correctly.

Implementation uses the installed .NET Framework compiler and WPF, with a pinned official WebView2 dependency. No global SDK installation. Browser data stays under LocalAppData/MicaBrowser; smoke tests use a separate temporary profile. Persistent tabs, bookmarks and bounded history use atomic JSON writes. Only HTTP(S) addresses may be entered; internal home is native UI, and script/file/custom schemes are rejected. Website content has no native application bridge.

Version one includes back/forward/reload, tabs, pinning, two spaces, bookmarks, history search, theme switching, keyboard shortcuts, loading/error states, native download UI and memory metrics. Extensions, sync and split view are outside this version.

Validation: behavior tests for URL classification, ranking, duplicate suppression, tab lifecycle and persistence; compile Windows executable; native application smoke test with isolated profile; render home and Spotlight to PNG for visual inspection.
