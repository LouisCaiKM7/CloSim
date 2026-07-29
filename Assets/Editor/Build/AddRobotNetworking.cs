// CloSim Online Multiplayer — one-shot Editor batch fix-up: bakes the three network components
// (Mirror.NetworkIdentity, Mirror.NetworkTransformUnreliable, Online.Sync.RobotNetworkController) onto
// every spawnable robot prefab under Assets/Resources/Robots/Rebuilt and Assets/Resources/Robots/Reefscape.
//
// WHY THIS EXISTS: Online.Sync.MatchSpawnManager.SpawnAllRobots() / RegisterSpawnablePrefabs() both skip any
// robot prefab whose GetComponent<NetworkIdentity>() == null ("has no NetworkIdentity; cannot server-spawn"),
// which is why a hosted match currently spawns no robots — none of the ~90 robot prefabs carry the required
// Mirror components yet. See Online.Sync.RobotNetworkController's header comment for the exact required
// prefab shape (NetworkIdentity, NetworkTransformUnreliable [ServerToClient, sync position+rotation of the
// root only], RobotNetworkController).
//
// THE ASSETID GOTCHA: Mirror keeps a prefab's NetworkIdentity `_assetId` serialized field at 0 in the .prefab
// file and normally recomputes it in-editor from the asset GUID (NetworkIdentity.SetupIDs() -> AssignAssetID,
// via OnValidate / the assetId getter) using AssetDatabase, which does not exist at runtime — so a prefab
// shipped with `_assetId: 0` is rejected by RegisterPrefab/Spawn in a build. This exact problem was already
// hit and fixed once by hand for Assets/Resources/Online/RoomServiceNet.prefab (see git history: "wip(netcode):
// client-ready + room prefab assetId"), which wrote Mirror's own formula
//   assetId = NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(path)))   // = (uint)guid.GetHashCode()
// directly into the prefab's serialized `_assetId` field so it round-trips into the build. This script does
// the same thing, but through SerializedObject instead of hand-editing YAML, for every robot prefab, and
// verifies the value actually persisted by reloading the asset afterwards. If the SerializedObject write
// somehow fails to persist (belt-and-braces only — not expected), it falls back to patching the prefab's YAML
// text directly, exactly like the manual RoomServiceNet.prefab fix did.
//
// We deliberately do NOT rely on NetworkIdentity's own OnValidate/SetupIDs auto-assignment: whether
// PrefabUtility.IsPartOfPrefabAsset(root) is true for a GameObject loaded via PrefabUtility.LoadPrefabContents
// is not something we can verify without running Unity, and if it were false, SetupIDs() would treat the
// object as a *scene* object instead of a prefab and could assign a bogus non-zero sceneId while clearing
// _assetId back to 0 — the opposite of what we want. So this script computes and writes both fields
// explicitly and deterministically instead of depending on that internal Mirror editor behavior.
//
// Idempotent: safe to re-run. Existing components are left in place (only missing ones are added), and the
// assetId/sceneId/NetworkTransform fields are only rewritten (and the prefab only re-saved) if they don't
// already match the expected values.
//
// Batchmode usage:
//   "<UnityEditor>" -batchmode -quit -nographics \
//       -projectPath "<repo root>" \
//       -executeMethod AddRobotNetworking.Run \
//       -logFile "-"
//
// NOTE: the class is intentionally in the GLOBAL namespace so the -executeMethod target is exactly
// "AddRobotNetworking.Run" (matches the CloSimBuild.cs convention). This file lives under Assets/Editor/, so
// it compiles into the predefined Assembly-CSharp-Editor assembly and needs no .asmdef.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Mirror;
using Online.Sync;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Static Editor batch helper that adds the required Mirror networking components (+ a baked, non-zero
/// assetId) to every robot prefab under Assets/Resources/Robots/Rebuilt and .../Reefscape. Additive only:
/// never touches meshes, gameplay components, or tuning on the robots.
/// </summary>
public static class AddRobotNetworking
{
    private static readonly string[] RobotFolders =
    {
        "Assets/Resources/Robots/Rebuilt",
        "Assets/Resources/Robots/Reefscape",
    };

    private enum PrefabResult
    {
        Unchanged,
        Modified,
        Failed
    }

    /// <summary>
    /// The -executeMethod target. Scans every prefab under <see cref="RobotFolders"/>, adds the missing
    /// network components, bakes a valid assetId, and saves. Exits the Editor with code 0 on success (all
    /// prefabs end up with a valid non-zero assetId) or 1 on any failure, when running in batch mode.
    /// </summary>
    [MenuItem("CloSim/Online/Add Robot Networking Components")]
    public static void Run()
    {
        int scanned = 0;
        int modified = 0;
        int unchanged = 0;
        var failedPaths = new List<string>();

        try
        {
            foreach (string folder in RobotFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    Debug.LogWarning($"[AddRobotNetworking] Folder not found, skipping: {folder}");
                    continue;
                }

                string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
                Array.Sort(guids, (a, b) => string.CompareOrdinal(
                    AssetDatabase.GUIDToAssetPath(a), AssetDatabase.GUIDToAssetPath(b)));

                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    scanned++;

                    PrefabResult result = ProcessPrefab(path);
                    switch (result)
                    {
                        case PrefabResult.Modified:
                            modified++;
                            break;
                        case PrefabResult.Unchanged:
                            unchanged++;
                            break;
                        default:
                            failedPaths.Add(path);
                            break;
                    }
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[AddRobotNetworking] Scanned {scanned} robot prefab(s): {modified} modified, " +
                $"{unchanged} already OK, {failedPaths.Count} failed.");

            if (failedPaths.Count > 0)
            {
                Debug.LogError(
                    "[AddRobotNetworking] FAILED prefabs (missing a valid non-zero assetId or component):\n  " +
                    string.Join("\n  ", failedPaths));
                Exit(1);
                return;
            }

            if (scanned == 0)
            {
                Debug.LogError("[AddRobotNetworking] No robot prefabs found under the configured folders.");
                Exit(1);
                return;
            }

            Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AddRobotNetworking] EXCEPTION: {ex}");
            Exit(1);
        }
    }

    // ---------------------------------------------------------------- Per-prefab work

    private static PrefabResult ProcessPrefab(string path)
    {
        string guidStr = AssetDatabase.AssetPathToGUID(path);
        if (string.IsNullOrEmpty(guidStr))
        {
            Debug.LogError($"[AddRobotNetworking] No asset GUID for '{path}'; cannot bake assetId.");
            return PrefabResult.Failed;
        }

        uint expectedAssetId = NetworkIdentity.AssetGuidToUint(new Guid(guidStr));
        if (expectedAssetId == 0)
        {
            // Astronomically unlikely (GUID hash collision with 0), but Mirror treats 0 as "unassigned".
            Debug.LogError($"[AddRobotNetworking] Computed assetId is 0 for '{path}' (GUID {guidStr}).");
            return PrefabResult.Failed;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null)
        {
            Debug.LogError($"[AddRobotNetworking] PrefabUtility.LoadPrefabContents failed for '{path}'.");
            return PrefabResult.Failed;
        }

        bool dirty = false;

        try
        {
            // 1. Mirror.NetworkIdentity — required first; RobotNetworkController's [RequireComponent] would
            //    add it implicitly, but we add it explicitly first to control ordering and configure it below.
            var identity = root.GetComponent<NetworkIdentity>();
            if (identity == null)
            {
                identity = root.AddComponent<NetworkIdentity>();
                dirty = true;
            }

            // 2. Mirror.NetworkTransformUnreliable — Server To Client, sync position + rotation of the root
            //    only (matches RobotNetworkController's header contract).
            var netTransform = root.GetComponent<NetworkTransformUnreliable>();
            if (netTransform == null)
            {
                netTransform = root.AddComponent<NetworkTransformUnreliable>();
                dirty = true;
            }

            dirty |= ConfigureNetworkTransform(netTransform, root.transform);

            // 3. Online.Sync.RobotNetworkController — the per-robot NetworkBehaviour driving spawn/ownership.
            var controller = root.GetComponent<RobotNetworkController>();
            if (controller == null)
            {
                root.AddComponent<RobotNetworkController>();
                dirty = true;
            }

            // 4. Bake a valid, non-zero assetId (+ force sceneId back to 0 — prefabs must never carry a
            //    sceneId; see this file's header for why we don't rely on NetworkIdentity's own OnValidate
            //    auto-assignment here).
            dirty |= BakeIdentityIds(identity, expectedAssetId);

            if (dirty)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saveSuccess);
                if (!saveSuccess)
                {
                    Debug.LogError($"[AddRobotNetworking] SaveAsPrefabAsset reported failure for '{path}'.");
                    return PrefabResult.Failed;
                }
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // Verify against the actual asset on disk (not just our in-memory edit) — this is the part that
        // matters: a build only ever sees what got persisted to the .prefab file.
        if (!VerifyPersistedAssetId(path, expectedAssetId))
        {
            Debug.LogWarning(
                $"[AddRobotNetworking] assetId did not persist via SerializedObject for '{path}'; " +
                "falling back to direct YAML patch (same technique used for RoomServiceNet.prefab).");

            if (!TryRawYamlAssetIdFallback(path, expectedAssetId) || !VerifyPersistedAssetId(path, expectedAssetId))
            {
                Debug.LogError($"[AddRobotNetworking] Could not bake a valid assetId for '{path}' by any method.");
                return PrefabResult.Failed;
            }

            dirty = true;
        }

        Debug.Log($"[AddRobotNetworking] OK '{path}' assetId={expectedAssetId} ({(dirty ? "modified" : "already OK")})");
        return dirty ? PrefabResult.Modified : PrefabResult.Unchanged;
    }

    /// <summary>Configures the NetworkTransformUnreliable per RobotNetworkController's contract. Returns true if anything changed.</summary>
    private static bool ConfigureNetworkTransform(NetworkTransformUnreliable nt, Transform root)
    {
        bool changed = false;

        if (nt.syncDirection != SyncDirection.ServerToClient) { nt.syncDirection = SyncDirection.ServerToClient; changed = true; }
        if (!nt.syncPosition) { nt.syncPosition = true; changed = true; }
        if (!nt.syncRotation) { nt.syncRotation = true; changed = true; }
        if (nt.syncScale) { nt.syncScale = false; changed = true; }
        if (nt.target != root) { nt.target = root; changed = true; }

        return changed;
    }

    /// <summary>
    /// Force-writes NetworkIdentity's private serialized `_assetId` field (and resets the public `sceneId`
    /// field to 0, since this is a prefab and must never carry a scene id) via SerializedObject — bypassing
    /// Mirror's editor-only OnValidate/SetupIDs auto-assignment entirely so behavior does not depend on
    /// whether Unity considers the LoadPrefabContents root "part of a prefab asset". Returns true if the
    /// serialized data actually changed.
    /// </summary>
    private static bool BakeIdentityIds(NetworkIdentity identity, uint expectedAssetId)
    {
        var so = new SerializedObject(identity);

        SerializedProperty assetIdProp = so.FindProperty("_assetId");
        SerializedProperty sceneIdProp = so.FindProperty("sceneId");

        if (assetIdProp == null)
        {
            Debug.LogError(
                "[AddRobotNetworking] NetworkIdentity has no serialized '_assetId' field — " +
                "unexpected Mirror version; aborting bake for this prefab.");
            return false;
        }

        bool changed = false;

        if (assetIdProp.uintValue != expectedAssetId)
        {
            assetIdProp.uintValue = expectedAssetId;
            changed = true;
        }

        if (sceneIdProp != null && sceneIdProp.ulongValue != 0)
        {
            sceneIdProp.ulongValue = 0;
            changed = true;
        }

        if (changed)
            so.ApplyModifiedPropertiesWithoutUndo();

        return changed;
    }

    /// <summary>Reloads the prefab asset fresh from disk and confirms its NetworkIdentity carries the expected assetId.</summary>
    private static bool VerifyPersistedAssetId(string path, uint expectedAssetId)
    {
        GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (reloaded == null)
            return false;

        var identity = reloaded.GetComponent<NetworkIdentity>();
        if (identity == null)
            return false;

        var so = new SerializedObject(identity);
        SerializedProperty assetIdProp = so.FindProperty("_assetId");

        return assetIdProp != null && assetIdProp.uintValue == expectedAssetId && expectedAssetId != 0;
    }

    /// <summary>
    /// Last-resort fallback: patch the prefab's `_assetId: N` line directly in the YAML text and reimport —
    /// the same manual technique previously used to fix Assets/Resources/Online/RoomServiceNet.prefab (see
    /// this file's header). Only used if the normal SerializedObject write above somehow didn't persist.
    /// </summary>
    private static bool TryRawYamlAssetIdFallback(string path, uint expectedAssetId)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();
        string fullPath = Path.Combine(projectRoot, path);

        if (!File.Exists(fullPath))
        {
            Debug.LogError($"[AddRobotNetworking] YAML fallback: file not found at '{fullPath}'.");
            return false;
        }

        string text = File.ReadAllText(fullPath);
        const string pattern = @"_assetId:\s*\d+";
        string replacement = $"_assetId: {expectedAssetId}";

        if (!Regex.IsMatch(text, pattern))
        {
            Debug.LogError($"[AddRobotNetworking] YAML fallback: no '_assetId:' line found in '{path}'.");
            return false;
        }

        string patched = Regex.Replace(text, pattern, replacement);
        if (patched != text)
            File.WriteAllText(fullPath, patched);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return true;
    }

    private static void Exit(int code)
    {
        // Only force-exit in batch mode; interactive menu runs should not quit the Editor.
        if (Application.isBatchMode)
            EditorApplication.Exit(code);
    }
}
