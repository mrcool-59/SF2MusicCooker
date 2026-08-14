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
        public static void ExtractNumberAndName(string filename, out int number, out int moveFrom, out string name)
        {
            Match matchMove = regexMove.Match(filename);
            if (matchMove.Success)
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

        /*
        /// <summary>
        /// Read a set of numbers from file.
        /// </summary>
        public static HashSet<int> ReadNumberSet(string filename, char separator = ' ')
        {
            string[] parts = File.ReadAllText(filename).Split(separator);
            HashSet<int> set = new HashSet<int>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out int number)) set.Add(number);
            }
            return set;
        }
        */

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
            Regex regex = new Regex("^([a-zA-Z0-9_]+)\\:[ \t]*equ[ \t]*([0-9]+)$");
            string[] lines = File.ReadAllLines(filename);

            foreach (string line in lines)
            {
                string simplifiedLine = RemoveASMComment(line).Trim();
                Match match = regex.Match(simplifiedLine);
                if (match.Success)
                {
                    yield return (match.Groups[1].Value, int.Parse(match.Groups[2].Value));
                }
            }
        }

        /// <summary>
        /// Remove ASM comment from line and return the adjusted line.
        /// </summary>
        public static string RemoveASMComment(string line)
        {
            int index = line.IndexOf(';');
            if (index >= 0) line = line.Substring(0, index); // Strip comment
            return line;
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
    }
}