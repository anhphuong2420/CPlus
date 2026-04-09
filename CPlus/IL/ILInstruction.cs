using CPlusAST;

namespace CPlus.IL
{
    /// <summary>Base for all IL instructions. All subtypes are immutable records.</summary>
    public abstract record ILInstruction(Opcode Op);

    // -------------------------------------------------------------------------
    // No-operand instructions
    // Covers: ADD, SUB, MUL, DIV, NEG, NOT,
    //         EQ, NEQ, LT, LTE, GT, GTE, AND, OR,
    //         CONCAT, INT_TO_FLOAT,
    //         LOAD_THIS, LOAD_NULL,
    //         POP, DUP, RETURN, RETURN_VAL
    // -------------------------------------------------------------------------
    public record SimpleInstruction(Opcode Op) : ILInstruction(Op);

    // -------------------------------------------------------------------------
    // Index-operand instructions
    // Covers: LOAD_LOCAL, STORE_LOCAL, LOAD_ARG, STORE_ARG
    // -------------------------------------------------------------------------
    public record IndexInstruction(Opcode Op, int Index) : ILInstruction(Op);

    // -------------------------------------------------------------------------
    // Name-operand instructions
    // Covers: NEW                       (class name)
    //         LABEL, JUMP, JUMP_IF_TRUE, JUMP_IF_FALSE  (label name)
    // -------------------------------------------------------------------------
    public record NameInstruction(Opcode Op, string Name) : ILInstruction(Op);

    // -------------------------------------------------------------------------
    // Field instructions (LOAD_FIELD, STORE_FIELD)
    // Carries owner class + field type so Dump can emit ldfld/stfld with full sig.
    // -------------------------------------------------------------------------
    public record FieldInstruction(Opcode Op, string OwnerClass, string FieldName, DataType FieldType)
        : ILInstruction(Op);

    // -------------------------------------------------------------------------
    // Typed constant loads — one record per primitive type
    // -------------------------------------------------------------------------
    public record LoadIntInstruction(int Value)     : ILInstruction(Opcode.LOAD_CONST_INT);
    public record LoadFloatInstruction(float Value) : ILInstruction(Opcode.LOAD_CONST_FLOAT);
    public record LoadBoolInstruction(bool Value)   : ILInstruction(Opcode.LOAD_CONST_BOOL);
    public record LoadStrInstruction(string Value)  : ILInstruction(Opcode.LOAD_CONST_STR);

    // -------------------------------------------------------------------------
    // Method invocation
    // Covers: INVOKE (non-void), INVOKE_VOID (void)
    //
    // Stack layout before execution:
    //   [..., receiver, arg0, arg1, ..., argN-1]
    //
    // ClassName is the statically resolved owner class (from semantic analysis).
    // ArgCount does NOT include the receiver.
    // ReturnType and ParamTypes carry the full signature for ilasm call output.
    // -------------------------------------------------------------------------
    public record InvokeInstruction(
        Opcode Op,
        string ClassName,
        string MethodName,
        int ArgCount,
        DataType ReturnType,
        IReadOnlyList<DataType> ParamTypes)
        : ILInstruction(Op);
}
