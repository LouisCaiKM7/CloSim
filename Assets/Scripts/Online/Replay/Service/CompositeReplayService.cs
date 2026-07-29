// CloSim Online Multiplayer — replay service that unions the always-on LocalReplayService with the
// optional (still-blank-by-default per golden rule 2) AWS-backed ReplayServiceClient. This is the
// IReplayService the recorder and the Replays list UI should use instead of constructing
// ReplayServiceClient directly, so:
//   * Offline/single-player replays (local-only) show up and play back with zero AWS config.
//   * Online host replays keep uploading to S3 when configured, AND are also saved locally so the host
//     machine can watch its own online matches back even without a working backend.
//
// ListAsync merges both sources (local first — it is always available); GetAsync routes to whichever
// store owns the id (local files are recognizable via LocalReplayService.Has); UploadAsync always saves
// locally (that result is what the caller sees) and best-effort mirrors to the remote client when it is
// configured, never letting a remote hiccup turn an otherwise-successful local save into a failure.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Online.Contracts.Replay;

namespace Online.Replay.Service
{
    /// <summary>Combines the local on-disk replay store with the optional AWS-backed remote store.</summary>
    public sealed class CompositeReplayService : IReplayService
    {
        private readonly LocalReplayService _local;
        private readonly IReplayService _remote;

        public CompositeReplayService() : this(new LocalReplayService(), new ReplayServiceClient())
        {
        }

        public CompositeReplayService(LocalReplayService local, IReplayService remote)
        {
            _local = local ?? new LocalReplayService();
            _remote = remote;
        }

        /// <inheritdoc/>
        /// <remarks>Always true — the local store has no blank-config state; golden rule 2 only applies
        /// to the AWS-backed remote half, which degrades gracefully on its own when unconfigured.</remarks>
        public bool IsConfigured => true;

        /// <inheritdoc/>
        public async Task<ReplayUploadResult> UploadAsync(ReplayMetadata metadata, byte[] blob)
        {
            // Local save is always attempted first and is the result callers see — it is what makes
            // "every match gets a replay" true regardless of AWS configuration.
            ReplayUploadResult localResult = await _local.UploadAsync(metadata, blob);

            if (_remote != null && _remote.IsConfigured && localResult.ok)
            {
                try
                {
                    ReplayMetadata remoteMeta = metadata;
                    remoteMeta.replayId = ""; // let the remote backend assign its own id
                    await _remote.UploadAsync(remoteMeta, blob);
                }
                catch
                {
                    // Best-effort mirror only — the match already has a watchable local replay either way.
                }
            }

            return localResult;
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ReplayMetadata>> ListAsync(ReplayQuery query)
        {
            var merged = new List<ReplayMetadata>();

            IReadOnlyList<ReplayMetadata> localList = await _local.ListAsync(query);
            if (localList != null)
                merged.AddRange(localList);

            if (_remote != null && _remote.IsConfigured)
            {
                try
                {
                    IReadOnlyList<ReplayMetadata> remoteList = await _remote.ListAsync(query);
                    if (remoteList != null)
                        merged.AddRange(remoteList);
                }
                catch
                {
                    // Remote listing failures degrade to local-only results — never surfaced as an error.
                }
            }

            merged.Sort((a, b) => b.createdAt.CompareTo(a.createdAt)); // newest first

            if (query.limit > 0 && merged.Count > query.limit)
                merged.RemoveRange(query.limit, merged.Count - query.limit);

            return merged;
        }

        /// <inheritdoc/>
        public async Task<ReplayFetchResult> GetAsync(string replayId)
        {
            if (string.IsNullOrEmpty(replayId))
                return new ReplayFetchResult { ok = false, error = "replayId is empty" };

            if (_local.Has(replayId))
                return await _local.GetAsync(replayId);

            if (_remote != null && _remote.IsConfigured)
            {
                try
                {
                    return await _remote.GetAsync(replayId);
                }
                catch (Exception e)
                {
                    return new ReplayFetchResult { ok = false, error = e.Message };
                }
            }

            return new ReplayFetchResult { ok = false, error = "replay not found" };
        }
    }
}
