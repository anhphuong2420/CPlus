namespace CPlus.Exceptions.StaticErrors
{
    /// <summary>
    /// Thrown when no class in the program defines a valid entry point method.
    /// The entry point must be a public void method named 'main' with no parameters.
    /// </summary>
    public class MissingMainException : CplusStaticException
    {
        public MissingMainException()
            : base("No entry point found. Define 'public void main()' in one of your classes.")
        {
        }

        public override string ToString() => Message;
    }
}
