namespace CPlus.Exceptions
{
    /// <summary>
    /// Base for all CPlus semantic / static analysis errors.
    /// Catching this type covers: RedeclaredException, UndeclaredException,
    /// TypeMismatchInStatementException, TypeMismatchInExpressionException,
    /// CannotAssignToConstantException, IllegalConstantExpressionException,
    /// IllegalMemberAccessException.
    /// </summary>
    public class CplusStaticException : Exception
    {
        public CplusStaticException(string message) : base(message) { }
        public CplusStaticException(string message, Exception inner) : base(message, inner) { }
    }
}
