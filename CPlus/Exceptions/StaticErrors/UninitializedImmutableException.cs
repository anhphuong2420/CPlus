namespace CPlus.Exceptions.StaticErrors
{
    /// <summary>
    /// Thrown when an immut field or local variable is declared without an initializer.
    /// An immutable value must be assigned exactly once at the point of declaration.
    /// </summary>
    public class UninitializedImmutableException : CplusStaticException
    {
        public string Name { get; }
        public int Line { get; }
        public int Column { get; }

        public UninitializedImmutableException(string name, int line, int column)
            : base($"Immutable variable '{name}' must have an initializer (line {line}, column {column})")
        {
            Name = name;
            Line = line;
            Column = column;
        }

        public override string ToString() => Message;
    }
}
