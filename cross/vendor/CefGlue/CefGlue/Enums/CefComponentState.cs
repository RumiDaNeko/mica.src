//
// This file manually written from cef/include/internal/cef_types_component.h.
// C API name: cef_component_state_t.
//
namespace Xilium.CefGlue
{
    /// <summary>
    /// Component update states.
    /// </summary>
    public enum CefComponentState
    {
        New = 0,
        Checking = 1,
        CanUpdate = 2,
        Downloading = 3,
        Decompressing = 4,
        Patching = 5,
        Updating = 6,
        Updated = 7,
        UpToDate = 8,
        UpdateError = 9,
        Run = 10,
    }
}
