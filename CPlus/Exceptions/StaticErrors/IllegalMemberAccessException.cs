namespace CPlus.Exceptions.StaticErrors
{
    /// <summary>
    /// Thrown when attempting to access a private attribute or method.
    /// </summary>
    public class IllegalMemberAccessException : CplusStaticException
    {
        public Kind Kind { get; } // Attribute or Method
        public string MemberName { get; }
        public string ClassName { get; }
        public int Line { get; }
        public int Column { get; }

        public IllegalMemberAccessException(Kind kind, string memberName, string className, int line, int column)
            : base($"Illegal access to private {kind} '{memberName}' in class '{className}' at line {line}, column {column}")
        {
            Kind = kind;
            MemberName = memberName;
            ClassName = className;
            Line = line;
            Column = column;
        }

        public override string ToString()
        {
            return Message;
        }
    }
}
