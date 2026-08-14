using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SF2MusicCooker
{
    public sealed class LoopOptimizer
    {
        // This optimization problem is a fun brain teaser!

        /*
            Given a list of strings, say:
            A
            A
            A
            A
            A
            A
            A
            B

            Produce an equivalent list of strings that may save space by adding repetitions, like so:
            -- REPEAT x7 --
            A
            -- END REPEAT --
            B
        */

        private readonly Func<int, string> _beginMarker;
        private readonly Func<int, string> _endMarker;
        private readonly int _minWeight;
        private readonly int _windowSize;
        private readonly int _maxLength;
        private readonly int _maxRepeats;
        private readonly Func<string, string> _preTransformer;
        private readonly Func<string, string> _postTransformer;
        private readonly Func<string, int> _weighter;

        public string[] Optimize(string[] input)
        {
            List<string> transformedInput = new List<string>(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                string item = _preTransformer(input[i]);
                if (!string.IsNullOrEmpty(item)) transformedInput.Add(item);
            }
            return Execute(transformedInput.ToArray());
        }

        private string[] Execute(string[] input)
        {
            List<string> output = new List<string>(input.Length);
            int position = 0;

            while (position < input.Length)
            {
                int repeats = 0;
                int length = 0;

                for (int end = Math.Min(input.Length, position + _windowSize); end > position; end--)
                {
                    if (FindLoop(input, position, end, out length, out repeats)) break;
                }

                if (repeats == 0)
                {
                    // Can't do anything with this item
                    output.Add(_postTransformer(input[position]));
                    position++;
                }
                else
                {
                    Debug.Assert(length > 0);

                    // In case we are limiting the 'repeats'
                    repeats = Math.Min(_maxRepeats, repeats);

                    // Encode a loop
                    string beginMarker = _beginMarker(repeats);
                    if (beginMarker != null) output.Add(beginMarker);
                    for (int i = 0; i < length; i++) output.Add(_postTransformer(input[position + i]));
                    string endMarker = _endMarker(repeats);
                    if (endMarker != null) output.Add(endMarker);

                    // Skip ahead past the looped items
                    position += repeats * length;
                }
            }

            return output.ToArray();
        }

        // Return true if a valid loop is detected with its length and number of repeats.
        private bool FindLoop(string[] source, int start, int end, out int length, out int repeats)
        {
            int maxLength = Math.Min(_maxLength, (end - start) / 2); // It wouldn't make any sense to check longer lengths
            length = 1;
            repeats = 0;

            while (length <= maxLength)
            {
                repeats = TryLoop(source, start, end, length);
                if (repeats != 0) return true;
                length++;
            }
            return false;
        }

        // Return number of repeats if a valid loop can be made with the specified length. Otherwise return 0.
        private int TryLoop(string[] source, int start, int end, int length)
        {
            int totalLength = end - start;
            int repeats = totalLength / length;

            if (repeats * length == totalLength)
            {
                if (Fits(source, start, end, length, repeats))
                {
                    int weight = GetWeight(source, start, length, repeats);
                    if (weight >= _minWeight) return repeats;
                }
            }
            return 0;
        }

        private bool Fits(string[] source, int start, int end, int length, int repeats)
        {
            if (start + length * repeats != end) return false;

            int i = start;
            for (int r = 0; r < repeats; r++)
            {
                for (int j = 0; j < length; j++)
                {
                    if (source[start + j] != source[i]) return false;
                    i++;
                }
            }
            Debug.Assert(i == end);
            return true;
        }

        private int GetWeight(string[] source, int start, int length, int repeats)
        {
            int weight = 0;
            for (int i = 0; i < length; i++) weight += _weighter(source[start + i]);
            return weight * repeats;
        }

        public LoopOptimizer(Func<int, string> beginMarker, Func<int, string> endMarker, int minWeight, int windowSize = 256, int maxLength = int.MaxValue, int maxRepeats = int.MaxValue, Func<string, string> preTransformer = null, Func<string, string> postTransformer = null, Func<string, int> weighter = null)
        {
            if (minWeight < 1) throw new ArgumentOutOfRangeException(nameof(minWeight), "must be >= 1");
            if (windowSize < 0) throw new ArgumentOutOfRangeException(nameof(windowSize), "must be >= 0");
            if (maxLength < 1) throw new ArgumentOutOfRangeException(nameof(maxLength), "must be >= 1");
            if (maxRepeats < 2) throw new ArgumentOutOfRangeException(nameof(maxRepeats), "must be >= 2");

            _beginMarker = beginMarker ?? throw new ArgumentNullException(nameof(beginMarker));
            _endMarker = endMarker ?? throw new ArgumentNullException(nameof(endMarker));
            _minWeight = minWeight;
            _windowSize = windowSize;
            _maxLength = maxLength;
            _maxRepeats = maxRepeats;
            _preTransformer = preTransformer ?? (x => x);
            _postTransformer = postTransformer ?? (x => x);
            _weighter = weighter ?? (x => 1);
        }
    }
}