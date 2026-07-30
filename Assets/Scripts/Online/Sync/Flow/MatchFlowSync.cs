// CloSim Online Multiplayer — server-authoritative match flow replication (Phase 3, T3).
// Namespace: Online.Sync. File under Assets/Scripts/Online/Sync/Flow/.
//
// Drives the EXISTING Fms scheduled-start path (Fms.StartScheduled) so match timing/state is identical
// on host and every client with NO parallel timer:
//   * The server computes a single start time from the synchronized network clock (NetworkTime.time)
//     plus a small lead-in, applies it locally, and replicates it via a SyncVar.
//   * Every client applies the SAME start time and the SAME synchronized clock provider, so both host
//     and clients run Fms.ApplyScheduledState() off one shared clock.
//   * Fms.NetworkAuthoritative is raised on all networked machines so the legacy independent
//     client-side countdown in Fms.Update() never advances state online.
//
// SCENE SETUP (required, cannot be authored from code here): place a GameObject in the match scene
// carrying a Mirror NetworkIdentity plus this component (and ScoreSync). It must be a spawned networked
// object (scene object registered with the NetworkManager, or server-spawned) so SyncVars replicate.
//
// This file is additive and changes no scoring/match-rule math.

using Field.Core;
using Mirror;
using UnityEngine;

namespace Online.Sync
{
    [AddComponentMenu("CloSim/Sync/Match Flow Sync")]
    public class MatchFlowSync : NetworkBehaviour
    {
        [Tooltip("Seconds of lead-in between the server scheduling the match and MatchTimer starting. " +
                 "Gives late-ish clients time to receive the start time and show the pre-match countdown.")]
        [SerializeField] private double _startLeadInSeconds = 3d;

        [Tooltip("If true, the server automatically schedules the match once the scene Fms is available. " +
                 "Leave on for self-driving match scenes; the launcher may instead call ServerScheduleMatch().")]
        [SerializeField] private bool _autoScheduleOnServerStart = true;

        // Replicated single source of truth for the match start time on the synchronized network clock.
        // -1 = not scheduled yet. Hook fires on clients (including late joiners on initial sync).
        [SyncVar(hook = nameof(OnScheduledStartTimeChanged))]
        private double _scheduledStartTime = -1d;

        private Fms _fms;
        private bool _serverScheduled;

        public override void OnStartServer()
        {
            Fms.NetworkAuthoritative = true;
        }

        public override void OnStartClient()
        {
            Fms.NetworkAuthoritative = true;

            // Late-joiner safety: if the start time was already set before we spawned, apply it now.
            if (!isServer && _scheduledStartTime >= 0d)
            {
                ApplyScheduledStartOnClient(_scheduledStartTime);
            }
        }

        public override void OnStopClient()
        {
            // Leaving the network -> back to offline behavior (host also fires OnStopServer).
            Fms.NetworkAuthoritative = false;
        }

        public override void OnStopServer()
        {
            Fms.NetworkAuthoritative = false;
        }

        private double _finishedAt = -1d;
        private bool _returnedToLobby;
        private const double ReturnToLobbyDelaySeconds = 6d;

        private void Update()
        {
            // Server-only. Driven from Update (not OnStartServer) so the scene Fms has had a chance to run
            // its own OnEnable/Restart first, avoiding a schedule being wiped by Restart().
            if (!NetworkServer.active)
                return;

            // Persistent-room lifecycle: once the match reaches Finished, return the whole room to the lobby
            // after a short results pause so it can be reused for another match.
            TickReturnToLobbyOnFinish();

            if (!_autoScheduleOnServerStart || _serverScheduled)
                return;

            EnsureFms();
            if (_fms == null)
                return;

            ServerScheduleMatch();
        }

        [Server]
        private void TickReturnToLobbyOnFinish()
        {
            if (_returnedToLobby)
                return;

            EnsureFms();
            if (_fms == null)
                return;

            if (Fms.MatchState != MatchState.Finished)
            {
                _finishedAt = -1d;
                return;
            }

            if (_finishedAt < 0d)
            {
                _finishedAt = NetworkTime.time;
                return;
            }

            if (NetworkTime.time - _finishedAt < ReturnToLobbyDelaySeconds)
                return;

            _returnedToLobby = true;
            if (Online.Rooms.RoomService.Instance != null)
                Online.Rooms.RoomService.Instance.ServerReturnToLobby();
        }

        /// <summary>
        /// Server entry point: compute the authoritative start time from the synchronized clock and
        /// replicate it. Safe to call once; subsequent calls are ignored. Exposed so a match launcher
        /// can trigger the start explicitly instead of relying on auto-schedule.
        /// </summary>
        [Server]
        public void ServerScheduleMatch()
        {
            if (_serverScheduled)
                return;

            EnsureFms();
            if (_fms == null)
                return;

            double startTime = NetworkTime.time + _startLeadInSeconds;

            _serverScheduled = true;
            _scheduledStartTime = startTime; // replicates to clients (hook runs there)

            // Apply locally on the server/host; the SyncVar hook does not fire on the setter.
            _fms.StartScheduled(startTime, () => NetworkTime.time);
        }

        private void OnScheduledStartTimeChanged(double oldTime, double newTime)
        {
            if (isServer)
                return; // host already applied it in ServerScheduleMatch

            if (newTime < 0d)
                return;

            ApplyScheduledStartOnClient(newTime);
        }

        private void ApplyScheduledStartOnClient(double startTime)
        {
            EnsureFms();
            if (_fms == null)
                return;

            // Same start time + same synchronized clock as the host -> identical scheduled state everywhere.
            _fms.StartScheduled(startTime, () => NetworkTime.time);
        }

        private void EnsureFms()
        {
            if (_fms == null)
                _fms = FindAnyObjectByType<Fms>();
        }
    }
}
