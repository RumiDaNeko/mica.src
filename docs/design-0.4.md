# Mica 0.4 — sidebar, spaces, motion and extensions

Requested: remove fixed GH/YouTube/Gmail shortcuts; condensed search icon and favicon tab switcher; clearer menu; animated Spaces dropdown; richer motion including resize; Chrome extension support.

The sidebar keeps its only collapse control in the header. At 64 px it exposes search, Spaces, scrollable favicon tabs, new-tab, menu and theme. At 238 px it exposes named tabs and an actual Spaces dropdown. Legacy home/work buttons and YOUR SPACE label are removed. Personal and Work keep existing data and shared cookies.

Native popups overlay WebView2 safely. Spaces displays both choices, current selection and tab counts. Browser menu groups navigation, library, appearance and tools, with labeled commands and shortcut hints; extensions has a real manager. Popup open, hover/press, tab row entry, sidebar, Spotlight and native-page transitions use short easing, with Windows reduced-motion respected. Native window resizing remains attached to the cursor; resize interaction animates chrome feedback and responsive native content without lagging website view dimensions.

Favicon pipeline uses WebView2 GetFaviconAsync, frozen downsampled bitmaps and bounded profile-local cache. No third-party favicon requests. Mini tabs share the same tab state and context menus as full tabs.

WebView2 supports local unpacked extensions; direct Chrome Web Store/CRX installation and Chrome extension toolbar UI are not provided by the engine. Implement enablement, local installation, enumeration, enable/disable and removal honestly. Do not claim full Chrome compatibility. Validate a real harmless MV3 content script through install/disable/enable/remove on isolated loopback pages.

Validation: native regression for removed controls, mini search and tab switching, Spaces dropdown opening without switching, menu actions, interrupted animations, native resize hit testing; real favicon/cache checks; existing web navigation regression; core tests and live smoke. Output a new portable release without closing the user's running browser or altering their data.
