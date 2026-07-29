// CloSim Online Multiplayer — local on-disk replay store (ADDITIVE).
// Concrete implementation of Online.Contracts.Replay.IReplayService that persists replays to
// Application.persistentDataPath instead of the (still-blank, golden-rule-2) AWS backend. This is what
// lets OFFLINE / single-player matches produce watchable replays with ZERO external configuration:
// both recorders (Online.Replay.Recorder.MatchReplayRecorderRunner for online-host matches and
// Online.Replay.Recorder.OfflineMatchReplayRecorderRunner for offline/single-player matches) save here
// via CompositeReplayService, which additionally mirrors to the AWS-backed ReplayServiceClient when/if
// the user later supplies ReplayServiceConfig.BaseUrl.
//
// Layout (flat, one pair of files per replay):
//   <persistentDataPath>/Replays/<replayId>.meta.json  — JsonUtility-serialized ReplayMetadata
//   <persistentDataPath>/Replays/<replayId>.replay      — opaque codec blob (same bytes ReplayWriter
//                                                          produces / ReplayReader consumes; untouched
//                                                          wire format, per the task's hard constraint)
//
// Never throws to the caller (mirrors ReplayServiceClient's "never blocks a match" contract). Unlike the
// remote client, IsConfigured is always true here — local disk has no blank-config state.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Online.Contracts.Replay;
using UnityEngine;

namespace Online.Replay.Service
{
    /// <summary>Local-filesystem IReplayService. Always available; never requires user configuration.</summary>
    public sealed class LocalReplayService : IReplayService
    {
        private const string ReplaysFolderName = "Replays";
        private const string MetaExtension = ".meta.json";
        private const string BlobExtension = ".replay";

        private readonly string _root;

        /// <summary>Default constructor: stores under Application.persistentDataPath/Replays.</summary>
        public LocalReplayService() : this(Path.Combine(Application.persistentDataPath, ReplaysFolderName))
        {
        }

        /// <summary>Testable constructor — pass an explicit root directory.</summary>
        public LocalReplayService(string rootDirectory)
        {
            _root = rootDirectory;
        }

        /// <summary>Root directory this instance reads/writes (informational, e.g. for diagnostics/UI).</summary>
        public string RootDirectory => _root;

        /// <inheritdoc/>
        /// <remarks>Always true — local disk is always available (no blank-config state).</remarks>
        public bool IsConfigured => true;

        /// <inheritdoc/>
        public Task<ReplayUploadResult> UploadAsync(ReplayMetadata metadata, byte[] blob)
        {
            try
            {
                EnsureRoot();

                string replayId = string.IsNullOrEmpty(metadata.replayId)
                    ? GenerateReplayId()
                    : SanitizeId(metadata.replayId);

                metadata.replayId = replayId;
                blob ??= Array.Empty<byte>();

                File.WriteAllBytes(BlobPath(replayId), blob);
                File.WriteAllText(MetaPath(replayId), JsonUtility.ToJson(metadata));

                return Task.FromResult(new ReplayUploadResult
                {
                    ok = true,
                    replayId = replayId,
                    blobUrl = BlobPath(replayId),
                    error = ""
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalReplayService] Failed to save replay locally: {e.Message}");
                return Task.FromResult(new ReplayUploadResult
                {
                    ok = false,
                    replayId = metadata.replayId ?? "",
                    blobUrl = "",
                    error = e.Message
                });
            }
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<ReplayMetadata>> ListAsync(ReplayQuery query)
        {
            var results = new List<ReplayMetadata>();

            try
            {
                EnsureRoot();

                foreach (string metaFile in Directory.EnumerateFiles(_root, "*" + MetaExtension))
                {
                    ReplayMetadata? meta = TryReadMeta(metaFile);
                    if (meta == null)
                        continue;

                    ReplayMetadata m = meta.Value;

                    if (!string.IsNullOrEmpty(query.gameId) && !string.Equals(m.gameId, query.gameId, StringComparison.Ordinal))
                        continue;

                    results.Add(m);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalReplayService] Failed to list local replays: {e.Message}");
            }

            results.Sort((a, b) => b.createdAt.CompareTo(a.createdAt)); // newest first

            if (query.limit > 0 && results.Count > query.limit)
                results.RemoveRange(query.limit, results.Count - query.limit);

            return Task.FromResult<IReadOnlyList<ReplayMetadata>>(results);
        }

        /// <inheritdoc/>
        public Task<ReplayFetchResult> GetAsync(string replayId)
        {
            if (string.IsNullOrEmpty(replayId))
                return Task.FromResult(new ReplayFetchResult { ok = false, error = "replayId is empty" });

            try
            {
                string metaPath = MetaPath(replayId);
                string blobPath = BlobPath(replayId);

                if (!File.Exists(metaPath) || !File.Exists(blobPath))
                    return Task.FromResult(new ReplayFetchResult { ok = false, error = "replay not found locally" });

                ReplayMetadata? meta = TryReadMeta(metaPath);
                if (meta == null)
                    return Task.FromResult(new ReplayFetchResult { ok = false, error = "replay metadata is corrupt" });

                byte[] blob = File.ReadAllBytes(blobPath);

                return Task.FromResult(new ReplayFetchResult
                {
                    ok = true,
                    metadata = meta.Value,
                    blob = blob,
                    blobUrl = blobPath,
                    error = ""
                });
            }
            catch (Exception e)
            {
                return Task.FromResult(new ReplayFetchResult { ok = false, error = e.Message });
            }
        }

        /// <summary>
        /// True when replayId names a replay this local store owns. Used by CompositeReplayService to
        /// route GetAsync straight to disk without a failed round-trip through the remote client first.
        /// </summary>
        public bool Has(string replayId) => !string.IsNullOrEmpty(replayId) && File.Exists(MetaPath(replayId));

        private void EnsureRoot()
        {
            if (!Directory.Exists(_root))
                Directory.CreateDirectory(_root);
        }

        private string MetaPath(string replayId) => Path.Combine(_root, SanitizeId(replayId) + MetaExtension);
        private string BlobPath(string replayId) => Path.Combine(_root, SanitizeId(replayId) + BlobExtension);

        private static string SanitizeId(string replayId)
        {
            // replayId is either generated by us (GenerateReplayId, always filesystem-safe) or came back
            // out of our own metadata — strip anything not filesystem-safe defensively either way.
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = replayId.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                    chars[i] = '_';
            }
            return new string(chars);
        }

        private static string GenerateReplayId()
        {
            return "local-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static ReplayMetadata? TryReadMeta(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                return JsonUtility.FromJson<ReplayMetadata>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
