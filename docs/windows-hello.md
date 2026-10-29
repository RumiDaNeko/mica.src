# Windows Hello vault unlock

Opening Passwords & sync shows a themed unlock popup. Unlock with the master password; closing the popup closes the locked page. Closing the vault locks it again.

To enroll, select **Enable Windows Hello on this device**, enter the correct master password and complete the Windows security prompt. On later visits choose **Windows Hello**. Windows chooses PIN, face or fingerprint according to device configuration. The master password remains a fallback. **Forget Windows Hello on this device** deletes local enrollment.

Desktop verification uses UserConsentVerifier via IUserConsentVerifierInterop (Windows 11 build 22000 or later). Unsupported Windows versions or unavailable Hello leave password unlock usable. Mica does not collect a PIN, fingerprint or face data.

## Storage and limits

Opt-in enrollment stores a DPAPI CurrentUser-protected master password in the local profile's hello-unlock.bin with profile-bound entropy. The app requires successful native verification and a still-open popup before decrypting it. Enrollment never goes to Discord or the sync API. The encrypted vault format is unchanged.

This is app-enforced reauthentication, not a TPM/Hello-key-bound encryption scheme. Code running as the same Windows user may decrypt DPAPI data independently of Mica. Do not opt in if that threat model requires a master-password-only vault. Removing enrollment does not erase the encrypted vault.

## Verification

The release passed ten vault checks, including simulated Hello success/denial, closing during verification, unavailable Hello, forgetting enrollment, wrong master password, encrypted storage and existing fill/sync flows. The real availability API returned false in the test environment. Real PIN/biometric interaction needs testing on a configured Windows Hello device.

Native ABI source: https://learn.microsoft.com/en-us/windows/win32/api/userconsentverifierinterop/nf-userconsentverifierinterop-iuserconsentverifierinterop-requestverificationforwindowasync
