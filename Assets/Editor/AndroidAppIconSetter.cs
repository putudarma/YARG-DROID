using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Android build step: sets the app (launcher) icon to the YARG logo.
///
/// The project's Android icon slots in PlayerSettings are empty (the logo
/// texture is only set for Standalone), so UBA builds shipped with Unity's
/// default icon. This sets the icon from the real logo PNG
/// (Assets/Art/UI/AndroidAppIcon.png — the 512x512 YARG "Y" logo; the
/// repo's Icon_Stable.png is a Git LFS pointer, so a real copy is kept
/// here) before the player is built. It runs in OnPreprocessBuild, so the
/// icon is in the same APK it was built for.
/// </summary>
public class AndroidAppIconSetter : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    private const string ICON_PATH = "Assets/Art/UI/AndroidAppIcon.png";

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android)
        {
            return;
        }

        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(ICON_PATH);
        if (icon == null)
        {
            Debug.LogWarning("[AndroidAppIconSetter] Icon not found at " + ICON_PATH + " — keeping the default icon.");
            return;
        }

        PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Android, new Texture2D[] { icon });
        Debug.Log("[AndroidAppIconSetter] Android app icon set to " + ICON_PATH + ".");
    }
}
