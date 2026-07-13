using Core;

namespace Field.SeasonSpecific.Rebuilt
{
    public static class HumanPlayerRuntimeState
    {
        private static HumanPlayerType SelectedType { get; set; } = HumanPlayerType.Bucket;

        private static bool BlueHumanPlayerEnabled { get; set; }
        private static bool RedHumanPlayerEnabled { get; set; }

        public static void SetState(
            HumanPlayerType selectedType,
            bool blueEnabled,
            bool redEnabled
        )
        {
            SelectedType = selectedType;
            BlueHumanPlayerEnabled = blueEnabled;
            RedHumanPlayerEnabled = redEnabled;
        }

        public static bool IsDumperAllowed(bool isBlue)
        {
            if (SelectedType != HumanPlayerType.Dumper)
                return false;

            return isBlue ? BlueHumanPlayerEnabled : RedHumanPlayerEnabled;
        }
    }
}