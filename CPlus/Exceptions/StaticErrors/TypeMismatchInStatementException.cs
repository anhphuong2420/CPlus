namespace CPlus.Exceptions.StaticErrors
{
    /// <summary>
    /// Assignment statement: left hand side can be any type except void, right hand side is the same type or can coerce to LHS,
    ///                       float is super type of int.
    /// Call statement: only void type could be call alone. Passing parameters obey the rules for assignment statement.
    /// Return statement: return expression can be consider as RHS and LHS is the return type of the method.
    /// </summary>
    public class TypeMismatchInStatementException : CplusStaticException
    {
        public string StatementType { get; } // e.g., "Assignment", "Call", "Return"
        public string ExpectedType { get; }
        public string ActualType { get; }
        public int Line { get; }
        public int Column { get; }

        public TypeMismatchInStatementException(string statementType, string expectedType, string actualType, int line, int column)
            : base($"Type mismatch in {statementType} statement at line {line}, column {column}: expected {expectedType}, got {actualType}")
        {
            StatementType = statementType;
            ExpectedType = expectedType;
            ActualType = actualType;
            Line = line;
            Column = column;
        }

        public override string ToString()
        {
            return Message;
        }
    }
}
