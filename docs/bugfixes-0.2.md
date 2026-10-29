# Browser fixes 0.2

Reported: ConnectionAborted on navigation, sidebar cannot reopen after hiding, oversized caption controls, window cannot drag.

Reproduced before fixes using the real WPF window and WebView2: missing HTCAPTION on blank toolbar, caption button too large, missing visible sidebar restore control, and a served HTTP 200 document calling window.stop() hidden by the application error overlay. All four failed before changes.

Fixes: navigation-ID-aware completion policy distinguishes stopped usable responses from genuine network errors; permanent toolbar toggle; native WindowChrome caption region with interactive controls excluded through IsHitTestVisibleInChrome; compact caption buttons. Loading tabs are exempt from discard.

Validation: 29 pure behavior tests; native regression tests passed; live example.com, GitHub and signed-out Gmail checks passed. Tests do not establish authenticated Gmail mailbox behavior. Physical mouse dragging was not synthesized; native WM_NCHITTEST confirmed HTCAPTION for the drag region and HTCLIENT for controls.

Reference: Microsoft WebView2 documentation explicitly describes HTTP 200 plus ConnectionAborted when a page calls window.stop(): https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2navigationcompletedeventargs.httpstatuscode
