//
// This file manually written from cef/include/internal/cef_types_component.h.
// C API name: cef_component_update_error_t.
//
namespace Xilium.CefGlue
{
    /// <summary>
    /// Error codes for component update operations.
    /// </summary>
    public enum CefComponentUpdateError
    {
        None = 0,
        UpdateInProgress = 1,
        UpdateCanceled = 2,
        RetryLater = 3,
        ServiceError = 4,
        UpdateCheckError = 5,
        CrxNotFound = 6,
        InvalidArgument = 7,
        BadCrxDataCallback = 8,
    }
}
