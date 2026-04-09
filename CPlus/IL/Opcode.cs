namespace CPlus.IL
{
    public enum Opcode
    {
        // --- Constant loads ---
        LOAD_CONST_INT,
        LOAD_CONST_FLOAT,
        LOAD_CONST_BOOL,
        LOAD_CONST_STR,
        LOAD_NULL,

        // --- Local variables (declared in method body, separate index space from params) ---
        LOAD_LOCAL,
        STORE_LOCAL,

        // --- Parameters (index space separate from locals) ---
        LOAD_ARG,
        STORE_ARG,

        // --- Instance fields (always accessed through an object reference on the stack) ---
        LOAD_FIELD,
        STORE_FIELD,

        // --- This reference ---
        LOAD_THIS,

        // --- Object creation ---
        NEW,

        // --- Method invocation ---
        // Stack before: [..., receiver, arg0, arg1, ..., argN-1]
        INVOKE,       // non-void: pops receiver+args, pushes return value
        INVOKE_VOID,  // void:     pops receiver+args, pushes nothing

        // --- Arithmetic (binary, pop right then left, push result) ---
        ADD,
        SUB,
        MUL,
        DIV,

        // --- Unary ---
        NEG,  // arithmetic negation
        NOT,  // logical NOT (bool only)

        // --- Comparison (binary, result is always bool) ---
        EQ,
        NEQ,
        LT,
        LTE,
        GT,
        GTE,

        // --- Logical (binary, bool operands, bool result) ---
        AND,
        OR,

        // --- String ---
        CONCAT,       // string + string

        // --- Type conversion ---
        INT_TO_FLOAT, // widen int to float (implicit rule: int assignable to float)

        // --- Control flow ---
        LABEL,          // pseudo-instruction: marks a jump target (no runtime effect)
        JUMP,           // unconditional jump to label
        JUMP_IF_TRUE,   // pop bool, jump if true
        JUMP_IF_FALSE,  // pop bool, jump if false

        // --- Stack manipulation ---
        POP,  // discard top
        DUP,  // duplicate top

        // --- Return ---
        RETURN,     // void return (stack must be empty)
        RETURN_VAL, // pop top value and return it
    }
}
