namespace CPlus.Exceptions.StaticErrors
{
    /// <summary>
    /// Thrown when a class has a method named 'main' but its signature does not
    /// match the required entry point signature: public void main().
    /// </summary>
    public class InvalidMainSignatureException : CplusStaticException
    {
        public string ClassName { get; }
        public string ActualSignature { get; }
        public int Line { get; }
        public int Column { get; }

        public InvalidMainSignatureException(string className, string actualSignature, int line, int column)
            : base($"Invalid entry point in class '{className}': found '{actualSignature}' but expected 'public void main()' (line {line}, column {column})")
        {
            ClassName = className;
            ActualSignature = actualSignature;
            Line = line;
            Column = column;
        }

        public override string ToString() => Message;
    }
}
