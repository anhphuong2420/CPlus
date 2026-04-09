namespace CPlus.Exceptions.StaticErrors
{
    /// <summary>
    /// Thrown when there’s a type mismatch in an expression.
    /// </summary>
    public class TypeMismatchInExpressionException : CplusStaticException
    {
        public string Operator { get; } // e.g., "+", "=="
        public string ExpectedType { get; }
        public string ActualType { get; }
        public int Line { get; }
        public int Column { get; }

        public TypeMismatchInExpressionException(string @operator, string expectedType, string actualType, int line, int column)
            : base($"Type mismatch in expression with operator '{@operator}' at line {line}, column {column}: expected {expectedType}, got {actualType}")
        {
            Operator = @operator;
            ExpectedType = expectedType;
            ActualType = actualType;
            Line = line;
            Column = column;
        }

        public override string ToString()
        {
            return Message;
        }
    }
}
