using CPlusAST;
using CPlus.SematicChecker;

namespace CPlus.IL
{
    /// <summary>
    /// Translates a type-annotated CPlus AST into an ILProgram.
    /// Must run after SematicCheckerAuto so that Expression.ResolvedType is populated.
    /// </summary>
    public class ILGenerator : IASTVisitor<object?>
    {
        // ---- output ----
        private ILProgram _program = null!;

        // ---- per-class state ----
        private ILClass _currentClass = null!;
        private string _currentClassName = null!;

        // ---- per-method state ----
        private ILMethod _currentMethod = null!;
        private Dictionary<string, int> _paramIndex = new();
        private Dictionary<string, int> _localIndex = new();
        private int _labelCounter;

        // -----------------------------------------------------------------------
        // Public entry point
        // -----------------------------------------------------------------------

        public ILProgram Generate(CPlusAST.Program ast, CompileEnviroment env)
        {
            _program = new ILProgram();
            ast.Accept(this, env);
            return _program;
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private void Emit(ILInstruction instr) => _currentMethod.Instructions.Add(instr);
        private string NewLabel() => $"L{_labelCounter++}";

        /// <summary>
        /// Returns the class name that owns the method being called, by reading
        /// the statically resolved type of the receiver expression.
        /// </summary>
        private string ReceiverClassName(Expression expr)
        {
            if (expr.ResolvedType is ClassType ct)
                return ct.ClassName.Name;
            throw new InvalidOperationException(
                $"ILGenerator: cannot resolve class name — receiver has no ClassType ResolvedType ({expr})");
        }

        // -----------------------------------------------------------------------
        // Program / Class / Field  (structural — no instructions)
        // -----------------------------------------------------------------------

        public object? Visit(CPlusAST.Program node, CompileEnviroment env)
        {
            foreach (var cls in node.ClassDecls)
                cls.Accept(this, env);
            return null;
        }

        public object? Visit(ClassDecl node, CompileEnviroment env)
        {
            _currentClassName = node.Name.Name;
            _currentClass = new ILClass { Name = _currentClassName };

            foreach (var member in node.Members)
                member.Accept(this, env);

            _program.Classes.Add(_currentClass);
            return null;
        }

        public object? Visit(FieldDecl node, CompileEnviroment env)
        {
            _currentClass.Fields.Add(new ILField
            {
                Name        = node.Decl.Name.Name,
                Type        = node.Decl.DataType,
                IsPublic    = node.FieldModifier is PublicModifier,
                IsImmutable = node.Decl is ConstDecl,
            });
            return null;
        }

        // -----------------------------------------------------------------------
        // MethodDecl — builds param/local index tables, emits initializers + body
        // -----------------------------------------------------------------------

        public object? Visit(MethodDecl node, CompileEnviroment env)
        {
            _paramIndex    = new Dictionary<string, int>();
            _localIndex    = new Dictionary<string, int>();
            _labelCounter  = 0;

            var ilMethod = new ILMethod
            {
                Name       = node.Name.Name,
                ReturnType = node.ReturnType,
                IsPublic   = node.MethodModifier is PublicModifier,
            };

            // Build parameter table
            int pi = 0;
            foreach (var p in node.Params)
            {
                _paramIndex[p.Name.Name] = pi;
                ilMethod.Params.Add(new ILLocal { Name = p.Name.Name, Type = p.DataType, Index = pi });
                pi++;
            }

            // Build local table
            int li = 0;
            foreach (var d in node.Decls)
            {
                _localIndex[d.Name.Name] = li;
                ilMethod.Locals.Add(new ILLocal { Name = d.Name.Name, Type = d.DataType, Index = li });
                li++;
            }

            _currentMethod = ilMethod;

            // Emit initializers for locals that have a value expression
            foreach (var d in node.Decls)
            {
                if (d.Value != null)
                {
                    d.Value.Accept(this, env);
                    Emit(new IndexInstruction(Opcode.STORE_LOCAL, _localIndex[d.Name.Name]));
                }
            }

            // Emit statements
            foreach (var stmt in node.Statements)
                stmt.Accept(this, env);

            // Implicit RETURN for void methods that have no explicit return
            if (node.ReturnType is VoidType &&
                (_currentMethod.Instructions.Count == 0 ||
                 _currentMethod.Instructions[^1].Op != Opcode.RETURN))
            {
                Emit(new SimpleInstruction(Opcode.RETURN));
            }

            _currentClass.Methods.Add(ilMethod);
            return null;
        }

        // -----------------------------------------------------------------------
        // Statements
        // -----------------------------------------------------------------------

        public object? Visit(Return node, CompileEnviroment env)
        {
            if (node.Expression == null)
            {
                Emit(new SimpleInstruction(Opcode.RETURN));
            }
            else
            {
                node.Expression.Accept(this, env);
                Emit(new SimpleInstruction(Opcode.RETURN_VAL));
            }
            return null;
        }

        public object? Visit(Assign node, CompileEnviroment env)
        {
            if (node.LHS is FieldAccess fa)
            {
                // obj.field = expr  →  emit obj, emit value, STORE_FIELD
                fa.Obj.Accept(this, env);
                node.Expression.Accept(this, env);
                Emit(new NameInstruction(Opcode.STORE_FIELD, fa.FieldName.Name));
            }
            else if (node.LHS is ID id)
            {
                if (_paramIndex.TryGetValue(id.Name, out int paramSlot))
                {
                    node.Expression.Accept(this, env);
                    Emit(new IndexInstruction(Opcode.STORE_ARG, paramSlot));
                }
                else if (_localIndex.TryGetValue(id.Name, out int localSlot))
                {
                    node.Expression.Accept(this, env);
                    Emit(new IndexInstruction(Opcode.STORE_LOCAL, localSlot));
                }
                else
                {
                    // Must be a field of `this`
                    // Stack rule: obj must be below value for STORE_FIELD
                    Emit(new SimpleInstruction(Opcode.LOAD_THIS));
                    node.Expression.Accept(this, env);
                    Emit(new NameInstruction(Opcode.STORE_FIELD, id.Name));
                }
            }
            return null;
        }

        public object? Visit(CallMethodStmt node, CompileEnviroment env)
        {
            // Push receiver
            node.Obj.Accept(this, env);
            // Push arguments left-to-right
            foreach (var arg in node.Params)
                arg.Accept(this, env);

            var className = ReceiverClassName(node.Obj);
            Emit(new InvokeInstruction(Opcode.INVOKE_VOID, className, node.Method.Name, node.Params.Count()));
            return null;
        }

        // -----------------------------------------------------------------------
        // Expressions
        // -----------------------------------------------------------------------

        public object? Visit(BinaryOp node, CompileEnviroment env)
        {
            var leftType  = node.Left.ResolvedType;
            var rightType = node.Right.ResolvedType;

            // Emit left operand, then widen if needed
            node.Left.Accept(this, env);
            if (leftType is IntType && rightType is FloatType)
                Emit(new SimpleInstruction(Opcode.INT_TO_FLOAT));

            // Emit right operand, then widen if needed
            node.Right.Accept(this, env);
            if (leftType is FloatType && rightType is IntType)
                Emit(new SimpleInstruction(Opcode.INT_TO_FLOAT));

            // Emit operator
            Opcode opcode = node.Op switch
            {
                "+"  when leftType is StringType || rightType is StringType => Opcode.CONCAT,
                "+"  => Opcode.ADD,
                "-"  => Opcode.SUB,
                "*"  => Opcode.MUL,
                "/"  => Opcode.DIV,
                "==" => Opcode.EQ,
                "!=" => Opcode.NEQ,
                "<"  => Opcode.LT,
                "<=" => Opcode.LTE,
                ">"  => Opcode.GT,
                ">=" => Opcode.GTE,
                "&&" => Opcode.AND,
                "||" => Opcode.OR,
                _ => throw new InvalidOperationException($"ILGenerator: unknown binary operator '{node.Op}'")
            };
            Emit(new SimpleInstruction(opcode));
            return null;
        }

        public object? Visit(UnaryOp node, CompileEnviroment env)
        {
            node.Body.Accept(this, env);
            if (node.Op == "-")      Emit(new SimpleInstruction(Opcode.NEG));
            else if (node.Op == "!") Emit(new SimpleInstruction(Opcode.NOT));
            // unary "+" is identity — no instruction needed
            return null;
        }

        public object? Visit(CallExpression node, CompileEnviroment env)
        {
            // Push receiver
            node.Obj.Accept(this, env);
            // Push arguments left-to-right
            foreach (var arg in node.Params)
                arg.Accept(this, env);

            var className = ReceiverClassName(node.Obj);
            Emit(new InvokeInstruction(Opcode.INVOKE, className, node.Method.Name, node.Params.Count()));
            return null;
        }

        public object? Visit(NewExpression node, CompileEnviroment env)
        {
            Emit(new NameInstruction(Opcode.NEW, node.ClassName.Name));
            return null;
        }

        public object? Visit(FieldAccess node, CompileEnviroment env)
        {
            node.Obj.Accept(this, env);
            Emit(new NameInstruction(Opcode.LOAD_FIELD, node.FieldName.Name));
            return null;
        }

        public object? Visit(ID node, CompileEnviroment env)
        {
            if (_paramIndex.TryGetValue(node.Name, out int paramSlot))
            {
                Emit(new IndexInstruction(Opcode.LOAD_ARG, paramSlot));
            }
            else if (_localIndex.TryGetValue(node.Name, out int localSlot))
            {
                Emit(new IndexInstruction(Opcode.LOAD_LOCAL, localSlot));
            }
            else
            {
                // Field of `this`
                Emit(new SimpleInstruction(Opcode.LOAD_THIS));
                Emit(new NameInstruction(Opcode.LOAD_FIELD, node.Name));
            }
            return null;
        }

        public object? Visit(ThisLiteral node, CompileEnviroment env)
        {
            Emit(new SimpleInstruction(Opcode.LOAD_THIS));
            return null;
        }

        // ---- Literals ----

        public object? Visit(IntLiteral node, CompileEnviroment env)
        {
            Emit(new LoadIntInstruction(node.Value));
            return null;
        }

        public object? Visit(FloatLiteral node, CompileEnviroment env)
        {
            Emit(new LoadFloatInstruction(node.Value));
            return null;
        }

        public object? Visit(StringLiteral node, CompileEnviroment env)
        {
            Emit(new LoadStrInstruction(node.Value));
            return null;
        }

        public object? Visit(BooleanLiteral node, CompileEnviroment env)
        {
            Emit(new LoadBoolInstruction(node.Value));
            return null;
        }

        // ---- Stubs — these nodes are handled structurally above ----

        public object? Visit(PublicModifier node, CompileEnviroment env)          => null;
        public object? Visit(PrivateModifier node, CompileEnviroment env)         => null;
        public object? Visit(VarDecl node, CompileEnviroment env)                 => null;
        public object? Visit(ConstDecl node, CompileEnviroment env)               => null;
        public object? Visit(IntType node, CompileEnviroment env)                 => null;
        public object? Visit(FloatType node, CompileEnviroment env)               => null;
        public object? Visit(BooleanType node, CompileEnviroment env)             => null;
        public object? Visit(StringType node, CompileEnviroment env)              => null;
        public object? Visit(VoidType node, CompileEnviroment env)                => null;
        public object? Visit(ClassType node, CompileEnviroment env)               => null;
        public object? Visit(StaticAccessPrefixType node, CompileEnviroment env)  => null;
    }
}
