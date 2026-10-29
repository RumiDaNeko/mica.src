# Mica 0.8.1 — Consistent dialogs

Replaces the stock shortcut MessageBox with a themed owned window: grouped shortcuts, individual keycaps, custom close control, theme-aware scrollbar, drag surface, Escape/Done dismissal and reduced-motion-aware entry animation. Reopening activates the existing window.

The two vault replacement prompts use the same custom surface. They remain modal and preserve explicit consent; Cancel is focused/default, closing returns no consent. No MessageBox calls remain in src. Native OS file pickers are unchanged.

Validation: final build and three native dialog checks passed, covering singleton/cleanup, light/dark rendering, and both confirmation results with Cancel default. Light/dark shortcuts and confirmation captures were inspected. Tests used an isolated profile and did not replace any real vault data.
