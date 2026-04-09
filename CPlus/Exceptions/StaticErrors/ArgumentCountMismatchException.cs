namespace CPlus.Exceptions.StaticErrors
{
    /// <summary>
    /// Thrown when a method is called with the wrong number of arguments.
    /// Distinct from a type mismatch: the arity itself is incorrect.
    /// </summary>
    public class ArgumentCountMismatchException : CplusStaticException
    {
        public string MethodName { get; }
        public int Expected { get; }
        public int Actual { get; }
        public int Line { get; }
        public int Column { get; }

        public ArgumentCountMismatchException(string methodName, int expected, int actual, int line, int column)
            : base($"Method '{methodName}' expects {expected} argument(s) but got {actual} (line {line}, column {column})")
        {
            MethodName = methodName;
            Expected = expected;
            Actual = actual;
            Line = line;
            Column = column;
        }

        public override string ToString() => Message;
    }
}
