namespace CPlus.Exceptions.StaticErrors
{
    /// <summary>
    /// Thrown when the expression in a return statement is not assignable to
    /// the method's declared return type.
    /// </summary>
    public class ReturnTypeMismatchException : CplusStaticException
    {
        public string MethodName { get; }
        public string ExpectedType { get; }
        public string ActualType { get; }
        public int Line { get; }
        public int Column { get; }

        public ReturnTypeMismatchException(string methodName, string expectedType, string actualType, int line, int column)
            : base($"Method '{methodName}' must return '{expectedType}' but got '{actualType}' (line {line}, column {column})")
        {
            MethodName = methodName;
            ExpectedType = expectedType;
            ActualType = actualType;
            Line = line;
            Column = column;
        }

        public override string ToString() => Message;
    }
}
