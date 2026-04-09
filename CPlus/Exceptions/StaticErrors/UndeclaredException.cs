namespace CPlus.Exceptions.StaticErrors
{
    /// <summary>
    /// Thrown when an identifier (variable, constant, attribute, class, method) is used but not declared.
    /// </summary>
    public class UndeclaredException : CplusStaticException
    {
        public Kind Kind { get; }
        public string Name { get; }
        public int Line { get; }
        public int Column { get; }

        public UndeclaredException(Kind kind, string name, int line, int column)
            : base($"Undeclared {kind}: '{name}' at line {line}, column {column}")
        {
            Kind = kind;
            Name = name;
            Line = line;
            Column = column;
        }

        public override string ToString()
        {
            return Message;
        }
    }
}
