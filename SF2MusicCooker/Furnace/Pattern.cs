namespace SF2MusicCooker.Furnace
{
    public sealed class Pattern
    {
        private readonly PatternCell[] cells;

        public int Rows { get { return cells.Length; } }

        public Pattern(int rows)
        {
            cells = new PatternCell[rows];
        }

        /// <summary>
        /// Get a cell from the pattern. This method will always return a not-null PatternCell.
        /// </summary>
        public PatternCell Get(int row)
        {
            return cells[row] ?? PatternCell.Empty;
        }

        /// <summary>
        /// Set a cell from the pattern. Passing null will clear the cell.
        /// </summary>
        public void Set(int row, PatternCell cell)
        {
            if (cell != null && cell.IsEmpty) cell = null;
            cells[row] = cell;
        }

        /// <summary>
        /// Multiply pattern rows by N and return the resulting pattern.
        /// </summary>
        public Pattern Multiply(int n)
        {
            Pattern pattern = new Pattern(Rows * n);
            for (int i = 0; i < Rows; i++)
            {
                if (cells[i] != null)
                {
                    PatternCell newCell = cells[i].Multiply(n, out PatternCell beforeNextCell);
                    pattern.Set(i * n, newCell);
                    pattern.Set((i + 1) * n - 1, beforeNextCell);
                }
            }
            return pattern;
        }
    }
}