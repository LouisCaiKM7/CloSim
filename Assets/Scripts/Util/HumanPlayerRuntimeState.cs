namespace Util
{
    public static class HumanPlayerRuntimeState
    {
        public static HumanPlayerType SelectedType { get; private set; } = HumanPlayerType.Bucket;

        public static bool BlueHumanPlayerEnabled { get; private set; }
        public static bool RedHumanPlayerEnabled { get; private set; }

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