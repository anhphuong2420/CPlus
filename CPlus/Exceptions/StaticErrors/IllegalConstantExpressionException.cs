namespace CPlus.Exceptions.StaticErrors
{
    /// <summary>
    /// Thrown when a constant is assigned a non-static (non-literal) value.
    /// </summary>
    public class IllegalConstantExpressionException : CplusStaticException
    {
        public string ConstantName { get; }
        public string Expression { get; }
        public int Line { get; }
        public int Column { get; }

        public IllegalConstantExpressionException(string constantName, string expression, int line, int column)
            : base($"Illegal constant expression for '{constantName}' at line {line}, column {column}: expected literal, got '{expression}'")
        {
            ConstantName = constantName;
            Expression = expression;
            Line = line;
            Column = column;
        }

        public override string ToString()
        {
            return Message;
        }
    }
}
