using CPlus.SematicChecker;

namespace CPlusAST
{
    public abstract class AST
    {
        public int Line { get; set; }
        public int Column { get; set; }
        public abstract TResult Accept<TResult>(IASTVisitor<TResult> visitor, CompileEnviroment env);
        public AST SetPosition((int, int) lineAndCol)
        {
            Line = lineAndCol.Item1;
            Column = lineAndCol.Item2;
            return this;
        }
    }

    public partial class Program : AST
    {
        public Program(IEnumerable<ClassDecl> classDecls)
        {
            ClassDecls = classDecls;
        }
        public IEnumerable<ClassDecl> ClassDecls { get; set; }
    }

    public partial class ClassDecl : AST
    {
        public ClassDecl(ID name, IEnumerable<Member> members)
        {
            Name = name;
            Members = members;
        }
        public ID Name { get; set; }
        public IEnumerable<Member> Members { get; set; }
    }

    public abstract partial class Modifier : AST { }
    public partial class PublicModifier : Modifier { }
    public partial class PrivateModifier : Modifier { }

    public abstract partial class Member : AST { }

    public partial class FieldDecl : Member
    {
        public FieldDecl(Modifier fieldModifier, StoreDecl decl)
        {
            FieldModifier = fieldModifier;
            Decl = decl;
        }
        public Modifier FieldModifier { get; set; }
        public StoreDecl Decl { get; set; }
    }

    public partial class MethodDecl : Member
    {
        public MethodDecl(Modifier methodModifier, DataType returnType, ID name, IEnumerable<VarDecl> paramss, IEnumerable<StoreDecl> decls, IEnumerable<Statement> statements)
        {
            MethodModifier = methodModifier;
            ReturnType = returnType;
            Name = name;
            Params = paramss;
            Decls = decls;
            Statements = statements;
        }
        public Modifier MethodModifier { get; set; }
        public ID Name { get; set; }
        public IEnumerable<VarDecl> Params { get; set; }
        public DataType ReturnType { get; set; }
        public IEnumerable<StoreDecl> Decls { get; set; }
        public IEnumerable<Statement> Statements { get; set; }

    }

    public abstract partial class StoreDecl : AST {
        public StoreDecl(DataType dataType, ID name, Expression value)
        {
            DataType = dataType;
            Name = name;
            Value = value;
        }
        public DataType DataType { get; set; }
        public ID Name { get; set; }
        public Expression? Value { get; set; }
    }
    public partial class VarDecl : StoreDecl
    {
        public VarDecl(DataType dataType, ID name, Expression value) : base(dataType, name, value)
        {
        }

    }
    public partial class ConstDecl : StoreDecl
    {
        public ConstDecl(DataType dataType, ID name, Expression value) : base(dataType, name, value)
        {
        }
    }



    public abstract partial class Statement : AST { }

    public abstract partial class Expression : Statement
    {
        /// <summary>
        /// Set by SematicCheckerAuto after type-checking. Used by ILGenerator
        /// to resolve the class name for INVOKE / INVOKE_VOID and to decide
        /// whether INT_TO_FLOAT widening is needed in binary expressions.
        /// </summary>
        public DataType? ResolvedType { get; set; }
    }

    public abstract partial class LHS : Expression { } // Left hand side



    public partial class BinaryOp : Expression 
    {
        public BinaryOp(string op, Expression left, Expression right)
        {
            Op = op;
            Left = left;
            Right = right;
        }
        public string Op { get; set; }
        public Expression Left { get; set; }
        public Expression Right { get; set; }
    }

    public partial class UnaryOp : Expression
    {
        public UnaryOp(string op, Expression body)
        {
            Op = op;
            Body = body;
        }
        public string Op { get; set; }
        public Expression Body { get; set; }
    }

    public partial class CallExpression : Expression
    {
        public CallExpression(Expression obj, ID method, IEnumerable<Expression> @params)
        {
            Obj = obj;
            Method = method;
            Params = @params;
        }
        public Expression Obj { get; set; }
        public ID Method { get; set; }
        public IEnumerable<Expression> Params { get; set; }
    }

    public partial class NewExpression : Expression
    {
        public NewExpression(ID className)
        {
            ClassName = className;
        }
        public ID ClassName { get; set; }
    }

    public partial class FieldAccess : LHS
    {
        public FieldAccess(Expression obj, ID fieldName)
        {
            Obj = obj;
            FieldName = fieldName;
        }
        public Expression Obj { get; set; }
        public ID FieldName { get; set; }
    }



    public abstract partial class Literal : Expression { }

    public partial class IntLiteral : Literal 
    {
        public IntLiteral(int value) 
        {
            Value = value;
        }
        public int Value { get; set; }
    }
    public partial class FloatLiteral : Literal 
    {
        public FloatLiteral(float value)
        {
            Value = value;
        }
        public float Value { get; set; }
    }
    public partial class StringLiteral : Literal 
    {
        public StringLiteral(string value)
        {
            Value = value;
        }
        public string Value { get; set; }
    }
    public partial class BooleanLiteral : Literal 
    {
        public BooleanLiteral(bool value)
        {
            Value = value;
        }
        public bool Value { get; set; }
    }
    public partial class ThisLiteral : Literal
    {

    }


    public partial class Assign : Statement
    {
        public Assign(LHS lhs, Expression expression)
        {
            LHS = lhs;
            Expression = expression;
        }
        public LHS LHS { get; set; }
        public Expression Expression { get; set; }
    }
    public partial class Return : Statement
    {
        public Return(Expression expression)
        {
            Expression = expression;
        }
        public Expression? Expression { get; set; }
    }
    public partial class CallMethodStmt : Statement
    {
        public CallMethodStmt(Expression obj, ID method, IEnumerable<Expression> @params)
        {
            Obj = obj;
            Method = method;
            Params = @params;
        }
        public Expression Obj { get; set; }
        public ID Method { get; set; }
        public IEnumerable<Expression> Params { get; set; }
    }




    public partial class ID : LHS
    {
        public ID(string name)
        {
            Name = name;
        }
        public string Name { get; set; }
    }



    public abstract partial class DataType : AST { }
    public partial class IntType : DataType { }
    public partial class FloatType : DataType { }
    public partial class BooleanType : DataType { }
    public partial class StringType : DataType { }
    public partial class VoidType : DataType { }
    public partial class ClassType : DataType {
        public ClassType(ID className)
        {
            ClassName = className;
        }

        public ID ClassName { get; set; }
    }

    public partial class StaticAccessPrefixType : DataType
    {
        public StaticAccessPrefixType(ID prefixName)
        {
            PrefixName = prefixName;
        }

        public ID PrefixName { get; set; }
    }
}
