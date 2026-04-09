namespace CPlus.Exceptions
{
    public class ParseException : CplusSyntaxException
    {
        public ParseException(string message) : base(message) { }
        public ParseException(string message, Exception inner) : base(message, inner) { }
    }
}