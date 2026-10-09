using UnityEngine;

namespace Mush.Testing.TrackTest
{
    /// <summary>Bounded, allocation-free pose history for the temporary follow experiment.</summary>
    internal sealed class TrackFollowPoseHistory
    {
        private const int Capacity = 2048;
        private readonly Sample[] samples = new Sample[Capacity];
        private int first;
        private int count;

        private struct Sample
        {
            public double time;
            public Vector3 position;
            public Quaternion rotation;
        }

        public void Reset(double time, Vector3 position, Quaternion rotation)
        {
            first = count = 0;
            Record(time - 0.6d, position, rotation);
            Record(time, position, rotation);
        }

        public void Record(double time, Vector3 position, Quaternion rotation)
        {
            if (count > 0 && time <= At(count - 1).time)
            {
                samples[(first + count - 1) % Capacity] = new Sample
                    { time = time, position = position, rotation = rotation };
                return;
            }
            if (count == Capacity)
            {
                first = (first + 1) % Capacity;
                count--;
            }
            samples[(first + count++) % Capacity] = new Sample
                { time = time, position = position, rotation = rotation };
        }

        public Pose Evaluate(double time)
        {
            if (count == 0) return new Pose(Vector3.zero, Quaternion.identity);
            if (time <= At(0).time) return ToPose(At(0));
            if (time >= At(count - 1).time) return ToPose(At(count - 1));
            int low = 0;
            int high = count - 1;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (At(middle).time <= time) low = middle;
                else high = middle;
            }
            Sample before = At(low);
            Sample after = At(high);
            float blend = (float)((time - before.time) / (after.time - before.time));
            return new Pose(Vector3.Lerp(before.position, after.position, blend),
                Quaternion.Slerp(before.rotation, after.rotation, blend));
        }

        private Sample At(int index) => samples[(first + index) % Capacity];
        private static Pose ToPose(Sample sample) => new Pose(sample.position, sample.rotation);
    }
}
