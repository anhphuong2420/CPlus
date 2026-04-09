namespace CPlus.Exceptions.StaticErrors
{
    /// <summary>
    /// Thrown when a statement can never be executed because a return statement
    /// always runs before it on every code path.
    /// </summary>
    public class UnreachableStatementException : CplusStaticException
    {
        public int Line { get; }
        public int Column { get; }

        public UnreachableStatementException(int line, int column)
            : base($"Unreachable statement at line {line}, column {column}")
        {
            Line = line;
            Column = column;
        }

        public override string ToString() => Message;
    }
}
