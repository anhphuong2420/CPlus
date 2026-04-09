namespace CPlus.Exceptions.StaticErrors
{
    /// <summary>
    /// Thrown when attempting to assign a value to a constant (immutable variable).
    /// </summary>
    public class CannotAssignToConstantException : CplusStaticException
    {
        public string ConstantName { get; }
        public int Line { get; }
        public int Column { get; }

        public CannotAssignToConstantException(string constantName, int line, int column)
            : base($"Cannot assign to constant '{constantName}' at line {line}, column {column}")
        {
            ConstantName = constantName;
            Line = line;
            Column = column;
        }

        public override string ToString()
        {
            return Message;
        }
    }
}
