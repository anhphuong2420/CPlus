namespace CPlus.Exceptions
{
    /// <summary>
    /// Base for all CPlus lexer and parser errors.
    /// Catching this type covers: ParseException, ErrorTokenException,
    /// IllegalEscapeException, UncloseStringException.
    /// </summary>
    public class CplusSyntaxException : Exception
    {
        public CplusSyntaxException(string message) : base(message) { }
        public CplusSyntaxException(string message, Exception inner) : base(message, inner) { }
    }
}
