namespace CPlus.Exceptions
{
    public class IllegalEscapeException : CplusSyntaxException
    {
        public IllegalEscapeException(string text, int line, int col) : base($"Illegal escape sequence: {text} at {line}:{col}") { }
    }
}
