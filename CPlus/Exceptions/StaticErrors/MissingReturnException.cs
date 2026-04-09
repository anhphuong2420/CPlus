namespace CPlus.Exceptions.StaticErrors
{
    /// <summary>
    /// Thrown when a non-void method has at least one code path that does not
    /// end with a return statement.
    /// </summary>
    public class MissingReturnException : CplusStaticException
    {
        public string MethodName { get; }
        public int Line { get; }
        public int Column { get; }

        public MissingReturnException(string methodName, int line, int column)
            : base($"Method '{methodName}' must return a value on all code paths (line {line}, column {column})")
        {
            MethodName = methodName;
            Line = line;
            Column = column;
        }

        public override string ToString() => Message;
    }
}
