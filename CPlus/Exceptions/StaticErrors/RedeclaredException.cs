namespace CPlus.Exceptions.StaticErrors
{
    /// <summary>
    /// Thrown when a variable, constant, attribute, class, method, or parameter is redeclared.
    /// </summary>
    public class RedeclaredException : CplusStaticException
    {
        public Kind Kind { get; }
        public string Name { get; }
        public int Line { get; }
        public int Column { get; }

        public RedeclaredException(Kind kind, string name, int line, int column)
            : base($"Redeclared {kind}: '{name}' at line {line}, column {column}")
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
