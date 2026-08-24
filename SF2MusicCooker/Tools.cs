using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace SF2MusicCooker
{
    public static class Tools
    {
        private static readonly Regex regex = new Regex("^([+@])music([0-9]+)(.+)");
        private static readonly Regex regexMove = new Regex("^\\+music([0-9]+)@([0-9]+)(.+)");

        /// <summary>
        /// Parse 'filename' according to one of the valid patterns explained in the README file and return music number, move from number (0 for add) and music name.
        /// Throws an exception otherwise.
        /// </summary>
        public static void ExtractNumberAndName(string filename, bool allowMove, out int number, out int moveFrom, out string name)
        {
            Match matchMove = allowMove ? regexMove.Match(filename) : null;
            if (matchMove != null && matchMove.Success)
            {
                number = int.Parse(matchMove.Groups[1].Value);
                moveFrom = int.Parse(matchMove.Groups[2].Value);
                name = Path.GetFileNameWithoutExtension(matchMove.Groups[3].Value).Trim(' ', '-', '_', '.');
            }
            else
            {
                Match match = regex.Match(filename);
                if (!match.Success)
                {
                    throw new FormatException("Furnace filename '" + filename + "' is incorrect" + Environment.NewLine
                        + "The name should start with '+musicXX' (add) or '@musicYY' (replace) or '+musicXX@YY' (move and replace)" + Environment.NewLine
                        + "Please refer to README file for details");
                }
                number = int.Parse(match.Groups[2].Value);
                moveFrom = match.Groups[1].Value == "@" ? number : 0;
                name = Path.GetFileNameWithoutExtension(match.Groups[3].Value).Trim(' ', '-', '_', '.');
            }
        }

        /// <summary>
        /// Get all active files (i.e: files with name not prefixed with '!') in the specified 'folder' whose filename match 'pattern'.
        /// </summary>
        public static FileInfo[] GetActiveFiles(string folder, string pattern)
        {
            DirectoryInfo info = new DirectoryInfo(folder);
            if (!info.Exists) return Array.Empty<FileInfo>();
            FileInfo[] files = info.GetFiles(pattern);
            return Array.FindAll(files, file => !file.Name.StartsWith("!"));
        }

        /// <summary>
        /// Given a possibly arbitrary string, return a close enough string that is legal in ASM code (i.e: to create labels).
        /// </summary>
        public static string GetASMValidName(string x)
        {
            StringBuilder b = new StringBuilder(x.Length);
            foreach (char c in x)
            {
                if (char.IsLetterOrDigit(c) && c < 0x100)
                {
                    b.Append(char.ToUpper(c));
                }
                else if (char.IsWhiteSpace(c))
                {
                    b.Append('_');
                }
            }
            if (b.Length == 0 || !char.IsLetter(b[0]))
            {
                b.Insert(0, '_');
            }
            return b.ToString();
        }

        /// <summary>
        /// Read a map of number=string lines from file. Lines can be ignored by prefixing them with ';'.
        /// </summary>
        public static Dictionary<int, string> ReadNumberStringMap(string filename, char separator = '=')
        {
            string[] lines = File.ReadAllLines(filename);
            Dictionary<int, string> map = new Dictionary<int, string>(lines.Length);
            foreach (string line in lines)
            {
                int index = line.IndexOf(separator);
                if (index >= 1)
                {
                    // Verify the line is not commented out
                    int disabledIndex = line.IndexOf(';');
                    if (disabledIndex >= 0 && disabledIndex < index) continue;

                    string key = line.Substring(0, index);
                    string value = line.Substring(index + 1);
                    map.Add(int.Parse(key), value);
                }
            }
            return map;
        }

        private static IEnumerable<(string key, int value)> IterateASMEnum(string filename)
        {
            Regex regex = new Regex("^([a-zA-Z0-9_]+)\\:[ \t]*equ[ \t]*([a-fA-F0-9hH]+)$");
            string[] lines = File.ReadAllLines(filename);

            foreach (string line in lines)
            {
                string simplifiedLine = RemoveASMComment(line).Trim();
                Match match = regex.Match(simplifiedLine);
                if (match.Success)
                {
                    yield return (match.Groups[1].Value, ConvertASMValue(match.Groups[2].Value));
                }
            }
        }

        /// <summary>
        /// Read all "label: equ value" ASM statements from a file and return a map.
        /// </summary>
        public static Dictionary<string, int> ReadASMEnumMap(string filename)
        {
            Dictionary<string, int> map = new Dictionary<string, int>();
            foreach ((string key, int value) in IterateASMEnum(filename)) map[key] = value;
            return map;
        }

        /// <summary>
        /// Read all "label: equ value" ASM statements from a file which is assumed to contain only one enum and return a reverse map.
        /// </summary>
        public static Dictionary<int, string> ReadASMEnumReverseMap(string filename)
        {
            Dictionary<int, string> map = new Dictionary<int, string>();
            foreach ((string key, int value) in IterateASMEnum(filename)) map.Add(value, key); // Check for no duplicate.
            return map;
        }

        /// <summary>
        /// Remove ASM comment from line and return the adjusted line (i.e: "foo bar ; something" becomes "foo bar").
        /// </summary>
        public static string RemoveASMComment(string line)
        {
            int index = line.IndexOf(';');
            if (index >= 0)
            {
                while (index > 0 && char.IsWhiteSpace(line[index - 1])) index--;
                line = line.Substring(0, index); // Strip comment
            }
            return line;
        }

        /// <summary>
        /// Remove ASM label from line and return the adjusted line (i.e: "something: foo bar" becomes "foo bar").
        /// </summary>
        public static string RemoveASMLabel(string line)
        {
            int index = line.IndexOf(':');
            if (index >= 0)
            {
                while (index < line.Length && char.IsWhiteSpace(line[index])) index++;
                line = line.Substring(index + 1); // Strip label
            }
            return line;
        }

        /// <summary>
        /// Convert an ASM numeric string value (e.g: "400" in decimal or "0CDh" in hexadecimal) to integer.
        /// </summary>
        public static int ConvertASMValue(string x)
        {
            if (x.EndsWith("h", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt32(x.Substring(0, x.Length - 1), 16);
            else
                return int.Parse(x);
        }

        /// <summary>
        /// Create a regex to capture an ASM numeric value.
        /// </summary>
        public static Regex CreateNumericElementRegex(string prefix)
        {
            return new Regex(Regex.Escape(prefix) + "[ \t]+([a-fA-F0-9hH]+)");
        }

        /// <summary>
        /// Get all elements that verify the provided regex. The regex must contain exactly 1 capture group.
        /// </summary>
        public static T[] GetAllElements<T>(string document, Regex elementRegex, Func<string, T> convertFunc)
        {
            if (elementRegex.GetGroupNumbers().Length != 2)
                throw new ArgumentException("regex must contain exactly 1 capture group", nameof(elementRegex));

            MatchCollection matches = elementRegex.Matches(document);
            T[] results = new T[matches.Count];
            for (int i = 0; i < results.Length; i++) results[i] = convertFunc(matches[i].Groups[1].Value);
            return results;            
        }

        /// <summary>
        /// Get all ASM numeric values for the specified ASM token.
        /// </summary>
        public static int[] GetAllNumericElements(string document, string token)
        {
            return GetAllElements(document, CreateNumericElementRegex(token), ConvertASMValue);
        }

        /// <summary>
        /// Get all elements that verify the provided regex. The regex must contain exactly 1 capture group.
        /// </summary>
        public static string[] GetAllStringElements(string document, Regex elementRegex)
        {
            return GetAllElements(document, elementRegex, x => x);
        }

        /// <summary>
        /// Get the name and version of this program in a user-friendly string.
        /// </summary>
        public static string GetAssemblyNameAndVersion()
        {
            Assembly assembly = Assembly.GetEntryAssembly();
            AssemblyFileVersionAttribute version = (AssemblyFileVersionAttribute)assembly.GetCustomAttribute(typeof(AssemblyFileVersionAttribute));
            AssemblyTitleAttribute title = (AssemblyTitleAttribute)assembly.GetCustomAttribute(typeof(AssemblyTitleAttribute));
            return title.Title + " v" + version.Version;
        }

        /// <summary>
        /// Give a detailed exception message by exploring inner exceptions.
        /// </summary>
        public static string Unwrap(Exception ex)
        {
            string message = ex.Message;
            while (ex.InnerException != null)
            {
                ex = ex.InnerException;
                message += " -> " + ex.Message;
            }
            message += Environment.NewLine + ex.StackTrace;
            return message;
        }

        /// <summary>
        /// Return hexadecimal representation of one byte.
        /// </summary>
        public static string Hex1(byte x)
        {
            return BitConverter.ToString(new byte[1] { x });
        }

        /// <summary>
        /// Return hexadecimal representation of byte array.
        /// </summary>
        public static string Hex(byte[] x)
        {
            return BitConverter.ToString(x).Replace("-", "");
        }

        /// <summary>
        /// Return hexadecimal representation of one byte that is suitable to be put into ASM output.
        /// </summary>
        public static string Hex1ASM(byte x)
        {
            return HexASM(new byte[1] { x });
        }

        /// <summary>
        /// Return hexadecimal representation of byte array that is suitable to be put into ASM output.
        /// </summary>
        public static string HexASM(byte[] x)
        {
            string h = Hex(x) + "h";
            if (!char.IsDigit(h[0])) h = "0" + h;
            return h;
        }

        /// <summary>
        /// Fill a buffer with a value.
        /// </summary>
        public static void Fill(byte[] buffer, int offset, int count, byte value)
        {
            if (offset < 0 || offset >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));

            int end = offset + count;
            if (count < 0 || end > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

            for (int i = offset; i < end; i++) buffer[i] = value;
        }

        /// <summary>
        /// Return true if 'buffer' contains 'values' at the specified 'offset'.
        /// </summary>
        public static bool Contains(byte[] buffer, int offset, byte[] values)
        {
            if (buffer.Length - offset < values.Length) throw new ArgumentOutOfRangeException(nameof(offset));

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != buffer[i + offset]) return false;
            }
            return true;
        }

        /// <summary>
        /// Return index if 'buffer' contains 'values', otherwise return -1.
        /// </summary>
        public static int IndexOf(byte[] buffer, byte[] values)
        {
            int end = buffer.Length - values.Length;
            for (int i = 0; i <= end; i++)
            {
                if (Contains(buffer, i, values)) return i;
            }
            return -1;
        }

        /// <summary>
        /// Pick item with the lowest score returned by evaluation function. In case of ties, the earliest item wins.
        /// </summary>
        public static T SelectMin<T>(IReadOnlyList<T> candidates, Func<T, int> fn)
        {
            if (fn == null)
                throw new ArgumentNullException(nameof(fn));

            if (candidates == null || candidates.Count == 0)
                return default;

            T best = candidates[0];
            int min = fn(best);
            for (int i = 1; i < candidates.Count; i++)
            {
                int score = fn(candidates[i]);
                if (score < min)
                {
                    min = score;
                    best = candidates[i];
                }
            }
            return best;
        }

        /// <summary>
        /// Pick item with the highest score returned by evaluation function. In case of ties, the earliest item wins.
        /// </summary>
        public static T SelectMax<T>(IReadOnlyList<T> candidates, Func<T, int> fn)
        {
            if (fn == null)
                throw new ArgumentNullException(nameof(fn));

            if (candidates == null || candidates.Count == 0)
                return default;

            T best = candidates[0];
            int max = fn(best);
            for (int i = 1; i < candidates.Count; i++)
            {
                int score = fn(candidates[i]);
                if (score > max)
                {
                    max = score;
                    best = candidates[i];
                }
            }
            return best;
        }
    }
}