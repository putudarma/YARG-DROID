#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;

namespace YARG.Helpers
{
    /// <summary>
    /// All-files access (MANAGE_EXTERNAL_STORAGE), needed to read the
    /// user-visible song folder /storage/emulated/0/YARG/songs on Android 11+.
    ///
    /// Ported from JordanSanchez06/YARG (YARG-Android branch,
    /// StoragePermissionHelper.cs), hardened: the fork called
    /// isExternalStorageManager with no guards, which throws on Android 10
    /// and below (the API does not exist there). Every call here is guarded
    /// so a missing API or a denied permission can never crash the game —
    /// callers must always work without the permission (the app-private
    /// song folder needs none).
    ///
    /// Note: this is only viable because the APK is sideloaded for personal
    /// use. A Play Store release using MANAGE_EXTERNAL_STORAGE needs a
    /// declaration and is normally rejected for a game like this.
    /// </summary>
    public static class StoragePermissionHelper
    {
        /// <summary>
        /// True on Android 11 (API 30) and newer, where all-files access exists.
        /// </summary>
        public static bool IsAllFilesAccessSupported()
        {
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    return version.GetStatic<int>("SDK_INT") >= 30;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StoragePermissionHelper] Cannot read SDK_INT: " + e.Message);
                return false;
            }
        }

        public static bool HasAllFilesAccess()
        {
            if (!IsAllFilesAccessSupported())
            {
                return false;
            }

            try
            {
                using (var env = new AndroidJavaClass("android.os.Environment"))
                {
                    return env.CallStatic<bool>("isExternalStorageManager");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StoragePermissionHelper] HasAllFilesAccess() failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Opens the system "All files access" page for this app. Returns
        /// true if a settings page was actually launched, false if no page
        /// could be opened (so the caller can retry later instead of
        /// assuming the user saw it). Never throws.
        /// </summary>
        public static bool PromptAllFilesAccess()
        {
            if (!IsAllFilesAccessSupported())
            {
                return false;
            }

            // First try the app-specific page (it opens straight onto this
            // app's toggle). Some OEM ROMs do not resolve that intent, so
            // fall back to the general "All files access" list page.
            if (LaunchAllFilesAccessPage(
                "android.settings.MANAGE_APP_ALL_FILES_ACCESS_PERMISSION",
                "package:" + Application.identifier))
            {
                return true;
            }

            Debug.LogWarning("[StoragePermissionHelper] App-specific all-files page did not open; trying the general list page.");
            return LaunchAllFilesAccessPage(
                "android.settings.MANAGE_ALL_FILES_ACCESS_PERMISSION",
                null);
        }

        private static bool LaunchAllFilesAccessPage(string action, string uri)
        {
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    AndroidJavaObject intent;
                    if (uri == null)
                    {
                        intent = new AndroidJavaObject("android.content.Intent", action);
                    }
                    else
                    {
                        intent = new AndroidJavaObject(
                            "android.content.Intent",
                            action,
                            new AndroidJavaClass("android.net.Uri")
                                .CallStatic<AndroidJavaObject>("parse", uri));
                    }

                    using (intent)
                    {
                        Debug.Log("[StoragePermissionHelper] Prompting for all-files access (" + action + ").");
                        activity.Call("startActivity", intent);
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StoragePermissionHelper] Could not open all-files page (" + action + "): " + e.Message);
                return false;
            }
        }
    }
}
#endif
