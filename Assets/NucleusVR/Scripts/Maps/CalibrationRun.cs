using System;

namespace NucleusVR.Maps
{
    public sealed class CalibrationRun
    {
        public CalibrationRun(int checkpointCount)
        {
            if (checkpointCount < 1)
                throw new ArgumentOutOfRangeException(nameof(checkpointCount));

            CheckpointCount = checkpointCount;
        }

        public int CheckpointCount { get; }
        public int NextStation { get; private set; }
        public bool InProgress => NextStation > 0 && !Completed;
        public bool Completed => NextStation > CheckpointCount;
        public double ElapsedSeconds { get; private set; }
        private double startSeconds;
        private double lastSeconds;

        // Station zero starts the run. Checkpoints can only be claimed in order.
        public bool TryActivate(int station, double seconds)
        {
            if (Completed || station != NextStation || double.IsNaN(seconds) ||
                double.IsInfinity(seconds) || (InProgress && seconds < lastSeconds))
                return false;

            if (station == 0)
                startSeconds = seconds;

            lastSeconds = seconds;
            NextStation++;
            if (Completed)
                ElapsedSeconds = seconds - startSeconds;

            return true;
        }

        public void Reset()
        {
            NextStation = 0;
            ElapsedSeconds = 0;
            startSeconds = 0;
            lastSeconds = 0;
        }
    }
}
