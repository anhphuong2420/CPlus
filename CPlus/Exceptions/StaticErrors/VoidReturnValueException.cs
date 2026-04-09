namespace CPlus.Exceptions.StaticErrors
{
    /// <summary>
    /// Thrown when a void method contains a return statement that returns a value.
    /// A void method may only use a bare 'return;' to exit early.
    /// </summary>
    public class VoidReturnValueException : CplusStaticException
    {
        public string MethodName { get; }
        public int Line { get; }
        public int Column { get; }

        public VoidReturnValueException(string methodName, int line, int column)
            : base($"Void method '{methodName}' cannot return a value (line {line}, column {column})")
        {
            MethodName = methodName;
            Line = line;
            Column = column;
        }

        public override string ToString() => Message;
    }
}
