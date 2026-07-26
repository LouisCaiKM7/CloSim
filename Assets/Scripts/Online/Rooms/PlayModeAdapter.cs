// CloSim Online Multiplayer — PlayMode <-> NetworkMatchConfig adapter (A3 Rooms & Modes).
// Namespace: Online.Rooms. Pure C#; NO Mirror / NO UnityEngine dependency so it is unit-testable
// in isolation. See Documentation/online/architecture.md §7 for the mapping table.
//
// GOLDEN RULE 7 (offline back-compat): the legacy Core.PlayMode enum stays canonical for OFFLINE
// play. Online play is canonical on (blueCount, redCount). This adapter is the ONLY bridge between
// the two representations and is strictly additive — it never mutates LoadMatch or the enum.
//
//   PlayMode (offline)   blueCount  redCount   notes
//   ------------------   ---------  --------   -----------------------
//   OneVsZero            1          0          same-alliance solo
//   TwoVsZero            2          0          co-op
//   ThreeVsZero          3          0          co-op
//   OneVsOne             1          1          versus
//   TwoVsTwo             2          2          versus
//
// Online-only asymmetric shapes (2v1, 3v1, 3v2, 3v3 + reverses) have NO PlayMode equivalent and are
// represented purely as counts. TryToPlayMode() is therefore a LOSSY narrowing that returns false
// for any shape without an enum; the count path is always valid.

using Core;
using Online.Contracts;

namespace Online.Rooms
{
    /// <summary>
    /// Pure mapping between the legacy offline <see cref="PlayMode"/> enum and the online
    /// count-based <see cref="NetworkMatchConfig"/>. Stateless; every method is deterministic.
    /// </summary>
    public static class PlayModeAdapter
    {
        // ----------------------------------------------------------------- enum -> counts (always valid)

        /// <summary>The (blueCount, redCount) shape for a legacy offline <see cref="PlayMode"/>.</summary>
        public static (int blueCount, int redCount) ToCounts(PlayMode mode)
        {
            switch (mode)
            {
                case PlayMode.OneVsZero:   return (1, 0);
                case PlayMode.TwoVsZero:   return (2, 0);
                case PlayMode.ThreeVsZero: return (3, 0);
                case PlayMode.OneVsOne:    return (1, 1);
                case PlayMode.TwoVsTwo:    return (2, 2);
                default:                   return (1, 0); // defensive: unknown enum => solo
            }
        }

        /// <summary>
        /// Builds a fully-populated <see cref="NetworkMatchConfig"/> from an offline <see cref="PlayMode"/>.
        /// Always valid. The extra fields (gameId/sceneName/humanPlayerType/allowSpectators) are supplied
        /// by the caller because the enum carries no such information.
        /// </summary>
        public static NetworkMatchConfig FromPlayMode(
            PlayMode mode,
            string gameId,
            string sceneName,
            int humanPlayerType = 0,
            bool allowSpectators = false)
        {
            (int blue, int red) = ToCounts(mode);
            return new NetworkMatchConfig
            {
                blueCount = blue,
                redCount = red,
                gameId = gameId ?? "",
                sceneName = sceneName ?? "",
                humanPlayerType = humanPlayerType,
                allowSpectators = allowSpectators
            };
        }

        // ----------------------------------------------------------------- counts -> enum (lossy)

        /// <summary>
        /// LOSSY narrowing back to the offline enum. Succeeds only for the five shapes that have a
        /// <see cref="PlayMode"/> value; online-only asymmetric shapes (2v1, 3v1, 3v2, 3v3, reverses,
        /// and any red-only Xv0) return false. Callers that need the online shape must keep the counts.
        /// </summary>
        public static bool TryToPlayMode(int blueCount, int redCount, out PlayMode mode)
        {
            switch (blueCount, redCount)
            {
                case (1, 0): mode = PlayMode.OneVsZero;   return true;
                case (2, 0): mode = PlayMode.TwoVsZero;   return true;
                case (3, 0): mode = PlayMode.ThreeVsZero; return true;
                case (1, 1): mode = PlayMode.OneVsOne;    return true;
                case (2, 2): mode = PlayMode.TwoVsTwo;    return true;
                default:     mode = PlayMode.OneVsZero;   return false;
            }
        }

        /// <summary>Convenience overload operating on a <see cref="NetworkMatchConfig"/>.</summary>
        public static bool TryToPlayMode(NetworkMatchConfig config, out PlayMode mode)
            => TryToPlayMode(config.blueCount, config.redCount, out mode);

        /// <summary>True if the shape round-trips through the offline enum without loss.</summary>
        public static bool HasPlayModeEquivalent(int blueCount, int redCount)
            => TryToPlayMode(blueCount, redCount, out _);

        // ----------------------------------------------------------------- display helpers

        /// <summary>
        /// Human-readable label for a shape, e.g. "3v3", "2v1", or "Co-op (2)" for a same-alliance Xv0.
        /// Used by the lobby mode selector. Presentation only — never drives logic.
        /// </summary>
        public static string ShapeLabel(int blueCount, int redCount)
        {
            if (redCount == 0 && blueCount == 0)
                return "Empty";
            if (redCount == 0)
                return blueCount == 1 ? "Solo (1)" : $"Co-op ({blueCount})";
            if (blueCount == 0)
                return redCount == 1 ? "Solo (1)" : $"Co-op ({redCount})";
            return $"{blueCount}v{redCount}";
        }

        /// <summary>Convenience overload operating on a <see cref="NetworkMatchConfig"/>.</summary>
        public static string ShapeLabel(NetworkMatchConfig config)
            => ShapeLabel(config.blueCount, config.redCount);
    }
}
