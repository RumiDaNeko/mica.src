# Mica 0.8.2

Ctrl+W now preserves pinned tabs, including the sole remaining tab. Normal and subsequently unpinned tabs still close. Explicit close controls remain available. Shortcut documentation and context-menu hints match this behavior.

Validation: the isolated native --pinned-test failed on the previous implementation, then all three checks passed on this executable: pinned preservation, normal closure, and unpin/last-tab fallback.
