namespace CPlus.Exceptions
{
    public class UncloseStringException : CplusSyntaxException
    {
        public UncloseStringException(string text, int line, int col) : base($"Unclosed string: {text} at {line}:{col}") { }
    }
}
