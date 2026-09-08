using System;

namespace SF2MusicCooker
{
    public static class EnvelopeGuesser
    {
        /// <summary>
        /// Given a list of levels, return the best matching envelope as well as the release tick.
        /// </summary>
        public static int Guess(byte[] levels, Envelope[] envelopes, out int release)
        {
            release = levels.Length;

            int bestError = int.MaxValue;
            int bestIndex = envelopes.Length - 1;

            bool IsVolatile(int index)
            {
                if (index <= 2 || index >= levels.Length - 2) return true; // Always consider edges volatile
                int v = levels[index];
                return levels[index - 2] != v || levels[index - 1] != v || levels[index + 1] != v || levels[index + 2] != v;
            }

            for (int r = levels.Length; r >= 0; r--)
            {
                if (IsVolatile(r))
                {
                    int index = Guess(levels, envelopes, r, out int error);
                    if (bestError > error)
                    {
                        bestError = error;
                        bestIndex = index;
                        release = r;
                    }
                }
            }

            return bestIndex;
        }

        /// <summary>
        /// Given a list of levels and a provided release tick, return the best matching envelope.
        /// </summary>
        public static int Guess(byte[] levels, Envelope[] envelopes, int release)
        {
            return Guess(levels, envelopes, release, out _);
        }

        /// <summary>
        /// Given an envelope, return the best matching envelope.
        /// </summary>
        public static int Guess(Envelope envelope, Envelope[] envelopes)
        {
            int padAttack = Math.Max(0, 20 - envelope.Attack.Length);
            int padRelease = Math.Max(0, 10 - envelope.Release.Length);
            byte[] levels = envelope.ToArray(padAttack, padRelease);
            return Guess(levels, envelopes, envelope.Attack.Length + padAttack, out _);
        }

        private static int Guess(byte[] levels, Envelope[] envelopes, int release, out int error)
        {
            error = int.MaxValue;
            int index = envelopes.Length - 1;

            for (int i = 0; i < envelopes.Length; i++)
            {
                int candidateError = Error(levels, envelopes[i], release);
                if (error > candidateError || (error == candidateError && envelopes[i].Length < envelopes[index].Length))
                {
                    error = candidateError;
                    index = i;
                }
            }

            return index;
        }

        private static int Error(byte[] levels, Envelope envelope, int release)
        {
            int error = 0;
            int attackEnd = Math.Min(release, 20);
            int releaseEnd = Math.Min(levels.Length, release + 10);

            for (int i = 0; i < attackEnd; i++)
            {
                int diff = levels[i] - envelope.Read(i, false);
                error += diff * diff;
            }
            for (int i = release; i < releaseEnd; i++)
            {
                int diff = levels[i] - envelope.Read(i - release, true);
                error += diff * diff;
            }
            return error;
        }
    }
}