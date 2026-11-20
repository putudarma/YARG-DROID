// Forces the Android target architectures at build time.
//
// UBA build #14 proved that editing ProjectSettings.asset by hand
// (AndroidTargetArchitectures: 1 -> 3) does NOT take effect: the APK
// still contained only lib/armeabi-v7a and no native BASS libraries,
// even though the build used the right commit (the landscape fix IS in
// the APK's manifest, screenOrientation=reverseLandscape). Whatever
// drops the serialized setting — editor import, build service, or a
// stale cache — this callback re-applies it right before the player
// is built, so the setting cannot be silently lost again.
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class AndroidArchitectureEnforcer : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android)
        {
            return;
        }

        var wanted = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
        var current = PlayerSettings.Android.targetArchitectures;
        if (current != wanted)
        {
            Debug.Log($"[AndroidArchitectureEnforcer] Android target architectures were {current}; forcing ARMv7|ARM64.");
            PlayerSettings.Android.targetArchitectures = wanted;
        }
        else
        {
            Debug.Log("[AndroidArchitectureEnforcer] Android target architectures already ARMv7|ARM64.");
        }
    }
}
