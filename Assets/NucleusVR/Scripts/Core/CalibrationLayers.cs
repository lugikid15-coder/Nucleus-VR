namespace NucleusVR.Core
{
    internal static class CalibrationLayers
    {
        // Reserved project layers. Keep in sync with TagManager.asset.
        public const int Course = 8;
        public const int Player = 9;
        public const int Hands = 10;
        public const int Props = 11;

        public const int EnvironmentMask = (1 << Course) | (1 << Props);
    }
}
