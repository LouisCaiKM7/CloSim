// CloSim Online Multiplayer — server-authoritative score replication (Phase 3, T3).
// Namespace: Online.Sync. File under Assets/Scripts/Online/Sync/Flow/.
//
// Replicates the authoritative scores + piece counters host->client. The host/server is the ONLY writer
// of the underlying score math (see FieldScorer/ScoreHolder server-gating); this component simply mirrors
// the resulting totals to clients via SyncVars:
//   * On the server/host: read the authoritative statics each frame and publish them into SyncVars.
//   * On a pure client: push the received SyncVar values into ScoreHolder (public) and FieldScorer
//     (via the additive FieldScorer.ApplyReplicatedCounters). Clients only ever receive, never mutate.
//
// SCENE SETUP: co-locate this with MatchFlowSync on a networked GameObject (NetworkIdentity) in the
// match scene. SyncVars handle late joiners (initial-state sync).
//
// This file is additive and changes no scoring/match-rule math.

using Field.Scoring;
using Mirror;
using UnityEngine;

namespace Online.Sync
{
    [AddComponentMenu("CloSim/Sync/Score Sync")]
    public class ScoreSync : NetworkBehaviour
    {
        [SyncVar] private int _blueScore;
        [SyncVar] private int _redScore;

        [SyncVar] private int _blueFuel;
        [SyncVar] private int _redFuel;

        [SyncVar] private int _blueCoral;
        [SyncVar] private int _redCoral;

        [SyncVar] private int _blueAlgae;
        [SyncVar] private int _redAlgae;

        private void Update()
        {
            if (NetworkServer.active)
            {
                // Host/server is authoritative: publish the live totals. Setting a SyncVar marks it dirty
                // and replicates only on change, so idle frames cost nothing.
                _blueScore = ScoreHolder.BlueScore;
                _redScore = ScoreHolder.RedScore;

                _blueFuel = FieldScorer.BlueFuel;
                _redFuel = FieldScorer.RedFuel;

                _blueCoral = FieldScorer.BlueCoral;
                _redCoral = FieldScorer.RedCoral;

                _blueAlgae = FieldScorer.BlueAlgae;
                _redAlgae = FieldScorer.RedAlgae;
            }
            else if (NetworkClient.active)
            {
                // Pure client: apply the replicated authoritative values for display only.
                ScoreHolder.BlueScore = _blueScore;
                ScoreHolder.RedScore = _redScore;

                FieldScorer.ApplyReplicatedCounters(
                    _blueFuel, _redFuel,
                    _blueCoral, _redCoral,
                    _blueAlgae, _redAlgae);
            }
        }
    }
}
