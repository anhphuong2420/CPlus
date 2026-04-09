using CPlus.Exceptions;
using CPlus.Exceptions.StaticErrors;
using CPlus.Helpers;
using CPlusAST;
using System.Linq;

namespace CPlus.SematicChecker
{
    public class SematicCheckerAuto : IASTVisitor<DataType>
    {
        public DataType Visit(CPlusAST.Program node, CompileEnviroment env)
        {
            var getEnvVisitor = new GetEnv();
            getEnvVisitor.Visit(node, env);

            foreach (var classDecl in node.ClassDecls)
                classDecl.Accept(this, env);

            CheckEntryPoint(node, env);

            return null;
        }

        // -----------------------------------------------------------------------
        // Entry-point validation — must have exactly one public void main() with
        // no parameters somewhere in the program.
        // -----------------------------------------------------------------------
        private static void CheckEntryPoint(CPlusAST.Program node, CompileEnviroment env)
        {
            foreach (var cls in node.ClassDecls)
            {
                var clsSymbol = env.SymbolTable.LookupClass(cls.Name.Name, cls.Line, cls.Column);

                if (!clsSymbol.Members.TryGetValue("main", out var mainSymbol))
                    continue;

                if (mainSymbol is not MethodSymbol mainMethod)
                    continue;

                // Found a method named main — validate its signature
                var visibility = mainSymbol.IsPublic ? "public" : "private";
                var retType    = mainSymbol.Type?.ToString() ?? "void";
                var paramStr   = string.Join(", ", mainMethod.Parameters.Select(p => p.DataType.ToString()));
                var actualSig  = $"{visibility} {retType} main({paramStr})";

                if (!mainSymbol.IsPublic || mainSymbol.Type is not VoidType || mainMethod.Parameters.Any())
                    throw new InvalidMainSignatureException(cls.Name.Name, actualSig, cls.Line, cls.Column);

                return; // valid entry point found
            }

            throw new MissingMainException();
        }

        // -----------------------------------------------------------------------

        public DataType Visit(ClassDecl node, CompileEnviroment env)
        {
            env.CurrentClass = env.SymbolTable.LookupClass(node.Name.Name, node.Line, node.Column);
            foreach (var mem in node.Members)
                mem.Accept(this, env);
            env.CurrentClass = null;
            return null;
        }

        public DataType Visit(PublicModifier node, CompileEnviroment env) => null;
        public DataType Visit(PrivateModifier node, CompileEnviroment env) => null;

        public DataType Visit(FieldDecl node, CompileEnviroment env)
        {
            node.Decl.Accept(this, env);
            return null;
        }

        public DataType Visit(MethodDecl node, CompileEnviroment env)
        {
            if (node.ReturnType is ClassType classType)
                env.SymbolTable.LookupClass(classType.ClassName.Name, node.Line, node.Column);

            env.CurrentMethod = node;
            env.SymbolTable.EnterScope();

            foreach (var par in node.Params)
            {
                if (par.DataType is ClassType paramClassType)
                    env.SymbolTable.LookupClass(paramClassType.ClassName.Name, par.Line, par.Column);
                env.SymbolTable.AddParamSymbol(new Symbol(par.Name.Name, par.DataType), par.Line, par.Column);
            }

            foreach (var decl in node.Decls)
                decl.Accept(this, env);

            // Non-void methods must have a return on all paths
            if (node.ReturnType is not VoidType)
            {
                var hasReturn = node.Statements.Any(CheckerHelper.HasReturnInAllPaths);
                if (!hasReturn)
                    throw new MissingReturnException(node.Name.Name, node.Line, node.Column);
            }

            // Visit statements, flagging any code after a return as unreachable
            bool returnSeen = false;
            foreach (var stmt in node.Statements)
            {
                if (returnSeen)
                    throw new UnreachableStatementException(stmt.Line, stmt.Column);

                stmt.Accept(this, env);

                if (CheckerHelper.HasReturnInAllPaths(stmt))
                    returnSeen = true;
            }

            env.SymbolTable.ExitScope();
            env.CurrentMethod = null;
            return null;
        }

        public DataType Visit(VarDecl node, CompileEnviroment env)
        {
            if (node.DataType is ClassType classType)
                env.SymbolTable.LookupClass(classType.ClassName.Name, node.Line, node.Column);

            if (env.CurrentMethod != null)
                env.SymbolTable.AddSymbol(new Symbol(node.Name.Name, node.DataType), node.Line, node.Column);

            if (node.Value != null)
            {
                var valType = node.Value.Accept(this, env);
                if (!CheckerHelper.CanAssign(node.DataType, valType, env))
                    throw new TypeMismatchInStatementException("Variable Declaration",
                        valType.ToString(), node.DataType.ToString(), node.Line, node.Column);
            }
            return null;
        }

        public DataType Visit(ConstDecl node, CompileEnviroment env)
        {
            if (node.DataType is ClassType classType)
                env.SymbolTable.LookupClass(classType.ClassName.Name, node.Line, node.Column);

            if (env.CurrentMethod != null)
                env.SymbolTable.AddSymbol(
                    new Symbol(node.Name.Name, node.DataType, isImmutable: true), node.Line, node.Column);

            // immut variable must always have an initializer
            if (node.Value == null)
                throw new UninitializedImmutableException(node.Name.Name, node.Line, node.Column);

            if (!CheckerHelper.IsConstantExpression(node.Value, env))
                throw new IllegalConstantExpressionException(
                    node.Name.Name, node.Value.ToString(), node.Line, node.Column);

            var valType = node.Value.Accept(this, env);
            if (!CheckerHelper.CanAssign(node.DataType, valType, env))
                throw new TypeMismatchInStatementException("Constant Declaration",
                    valType.ToString(), node.DataType.ToString(), node.Line, node.Column);

            return null;
        }

        public DataType Visit(Assign node, CompileEnviroment env)
        {
            var lhsType = node.LHS.Accept(this, env);
            var rhsType = node.Expression.Accept(this, env);

            if (node.LHS is ID id)
            {
                Symbol symbol = null;
                try
                {
                    symbol = env.SymbolTable.Lookup(id.Name, id.Line, id.Column);
                }
                catch (UndeclaredException)
                {
                    if (env.CurrentClass != null && env.CurrentClass.Members.TryGetValue(id.Name, out var member))
                        symbol = member;
                    else
                        throw;
                }
                if (symbol.IsImmutable)
                    throw new CannotAssignToConstantException(id.Name, node.Line, node.Column);
            }

            if (!CheckerHelper.CanAssign(lhsType, rhsType, env))
                throw new TypeMismatchInStatementException("Assignment",
                    rhsType.ToString(), lhsType.ToString(), node.Line, node.Column);

            return null;
        }

        public DataType Visit(Return node, CompileEnviroment env)
        {
            var returnType   = env.CurrentMethod.ReturnType;
            var methodName   = env.CurrentMethod.Name.Name;

            // return expr;  inside a void method
            if (returnType is VoidType && node.Expression != null)
                throw new VoidReturnValueException(methodName, node.Line, node.Column);

            // bare return;  inside a void method — fine
            if (returnType is VoidType)
                return null;

            // non-void method with bare return;
            if (node.Expression == null)
                throw new ReturnTypeMismatchException(
                    methodName, returnType.ToString(), "void", node.Line, node.Column);

            var exprType = node.Expression.Accept(this, env);
            if (!CheckerHelper.CanAssign(returnType, exprType, env))
                throw new ReturnTypeMismatchException(
                    methodName, returnType.ToString(), exprType.ToString(), node.Line, node.Column);

            return null;
        }

        public DataType Visit(CallMethodStmt node, CompileEnviroment env)
        {
            var classExprType = node.Obj.Accept(this, env);

            if (classExprType is not ClassType cType)
                throw new TypeMismatchInStatementException("Method call",
                    "Class type", classExprType?.ToString() ?? "null", node.Line, node.Column);

            var classSymbol  = env.SymbolTable.LookupClass(cType.ClassName.Name, node.Line, node.Column);
            var methodSymbol = classSymbol.Members.Values.OfType<MethodSymbol>()
                                    .FirstOrDefault(m => m.Name == node.Method.Name);

            if (methodSymbol == null)
                throw new UndeclaredException(new Method(), node.Method.Name, node.Line, node.Column);

            if (!methodSymbol.IsPublic && env.CurrentClass.Name != classSymbol.Name)
                throw new IllegalMemberAccessException(new Method(), node.Method.Name, classSymbol.Name, node.Line, node.Column);

            // Arity check
            var expected = methodSymbol.Parameters.Count();
            var actual   = node.Params.Count();
            if (actual != expected)
                throw new ArgumentCountMismatchException(node.Method.Name, expected, actual, node.Line, node.Column);

            // Argument type checks
            var paramDecls = methodSymbol.Parameters.ToList();
            var args       = node.Params.ToList();
            for (int i = 0; i < args.Count; i++)
            {
                var argType = args[i].Accept(this, env);
                if (!CheckerHelper.CanAssign(paramDecls[i].DataType, argType, env))
                    throw new TypeMismatchInStatementException($"Argument {i + 1} of {node.Method.Name}",
                        argType.ToString(), paramDecls[i].DataType.ToString(), node.Line, node.Column);
            }

            if (methodSymbol.Type is not VoidType)
                throw new TypeMismatchInStatementException("Method call statement",
                    "Void", methodSymbol.Type.ToString(), node.Line, node.Column);

            return null;
        }

        public DataType Visit(BinaryOp node, CompileEnviroment env)
        {
            var left  = node.Left.Accept(this, env);
            var right = node.Right.Accept(this, env);

            DataType result;

            if (node.Op == "&&" || node.Op == "||")
            {
                if (left is not BooleanType || right is not BooleanType)
                    throw new TypeMismatchInExpressionException(node.Op,
                        "Boolean", $"{left},{right}", node.Line, node.Column);
                result = new BooleanType();
            }
            else if (new[] { "+", "-", "*", "/", "%" }.Contains(node.Op))
            {
                if (CheckerHelper.CanAssign(new FloatType(), left, env) &&
                    CheckerHelper.CanAssign(new FloatType(), right, env))
                {
                    result = (left is FloatType || right is FloatType)
                        ? (DataType)new FloatType()
                        : new IntType();
                }
                else
                    throw new TypeMismatchInExpressionException(node.Op,
                        "Int/Float", $"{left},{right}", node.Line, node.Column);
            }
            else if (new[] { ">", "<", ">=", "<=" }.Contains(node.Op))
            {
                if (!CheckerHelper.CanAssign(new FloatType(), left, env) ||
                    !CheckerHelper.CanAssign(new FloatType(), right, env))
                    throw new TypeMismatchInExpressionException(node.Op,
                        "Int/Float", $"{left},{right}", node.Line, node.Column);
                result = new BooleanType();
            }
            else if (new[] { "==", "!=" }.Contains(node.Op))
            {
                if (!CheckerHelper.CanAssign(left, right, env) &&
                    !CheckerHelper.CanAssign(right, left, env))
                    throw new TypeMismatchInExpressionException(node.Op,
                        "Compatible Types", $"{left},{right}", node.Line, node.Column);
                result = new BooleanType();
            }
            else
            {
                result = left;
            }

            node.ResolvedType = result;
            return result;
        }

        public DataType Visit(UnaryOp node, CompileEnviroment env)
        {
            var bodyType = node.Body.Accept(this, env);
            DataType result;

            if (node.Op == "!")
            {
                if (bodyType is not BooleanType)
                    throw new TypeMismatchInExpressionException(node.Op,
                        "Boolean", bodyType.ToString(), node.Line, node.Column);
                result = new BooleanType();
            }
            else if (node.Op == "-")
            {
                if (bodyType is IntType)        result = new IntType();
                else if (bodyType is FloatType) result = new FloatType();
                else throw new TypeMismatchInExpressionException(node.Op,
                    "Int/Float", bodyType.ToString(), node.Line, node.Column);
            }
            else
            {
                result = bodyType;
            }

            node.ResolvedType = result;
            return result;
        }

        public DataType Visit(CallExpression node, CompileEnviroment env)
        {
            var classExprType = node.Obj.Accept(this, env);

            if (classExprType is not ClassType cType)
                throw new TypeMismatchInExpressionException("Method Call",
                    "ClassType", classExprType.ToString(), node.Line, node.Column);

            var classSymbol  = env.SymbolTable.LookupClass(cType.ClassName.Name, node.Line, node.Column);
            var methodSymbol = classSymbol.Members.Values.OfType<MethodSymbol>()
                                    .FirstOrDefault(m => m.Name == node.Method.Name);

            if (methodSymbol == null)
                throw new UndeclaredException(new Method(), node.Method.Name, node.Line, node.Column);

            if (!methodSymbol.IsPublic && env.CurrentClass.Name != classSymbol.Name)
                throw new IllegalMemberAccessException(new Method(), node.Method.Name, classSymbol.Name, node.Line, node.Column);

            // Arity check
            var expected = methodSymbol.Parameters.Count();
            var actual   = node.Params.Count();
            if (actual != expected)
                throw new ArgumentCountMismatchException(node.Method.Name, expected, actual, node.Line, node.Column);

            // Argument type checks
            var paramDecls = methodSymbol.Parameters.ToList();
            var args       = node.Params.ToList();
            for (int i = 0; i < args.Count; i++)
            {
                var argType = args[i].Accept(this, env);
                if (!CheckerHelper.CanAssign(paramDecls[i].DataType, argType, env))
                    throw new TypeMismatchInExpressionException($"Argument {i + 1} of {node.Method.Name}",
                        paramDecls[i].DataType.ToString(), argType.ToString(), node.Line, node.Column);
            }

            if (methodSymbol.Type is VoidType)
                throw new TypeMismatchInExpressionException("Method Call", "Non-Void", "Void", node.Line, node.Column);

            node.ResolvedType = methodSymbol.Type;
            return methodSymbol.Type;
        }

        public DataType Visit(NewExpression node, CompileEnviroment env)
        {
            env.SymbolTable.LookupClass(node.ClassName.Name, node.Line, node.Column);
            var result = new ClassType(node.ClassName);
            node.ResolvedType = result;
            return result;
        }

        public DataType Visit(FieldAccess node, CompileEnviroment env)
        {
            var objType = node.Obj.Accept(this, env);

            if (objType is not ClassType cType)
                throw new TypeMismatchInExpressionException("Field Access",
                    "ClassType", objType.ToString(), node.Line, node.Column);

            var classSymbol = env.SymbolTable.LookupClass(cType.ClassName.Name, node.Line, node.Column);
            if (!classSymbol.Members.TryGetValue(node.FieldName.Name, out var memberSymbol))
                throw new UndeclaredException(new Variable(), node.FieldName.Name, node.Line, node.Column);

            if (!memberSymbol.IsPublic && env.CurrentClass.Name != classSymbol.Name)
                throw new IllegalMemberAccessException(new Variable(), node.FieldName.Name, classSymbol.Name, node.Line, node.Column);

            node.ResolvedType = memberSymbol.Type;
            return memberSymbol.Type;
        }

        public DataType Visit(IntLiteral node, CompileEnviroment env)
        {
            node.ResolvedType = new IntType();
            return node.ResolvedType;
        }

        public DataType Visit(FloatLiteral node, CompileEnviroment env)
        {
            node.ResolvedType = new FloatType();
            return node.ResolvedType;
        }

        public DataType Visit(StringLiteral node, CompileEnviroment env)
        {
            node.ResolvedType = new StringType();
            return node.ResolvedType;
        }

        public DataType Visit(BooleanLiteral node, CompileEnviroment env)
        {
            node.ResolvedType = new BooleanType();
            return node.ResolvedType;
        }

        public DataType Visit(ThisLiteral node, CompileEnviroment env)
        {
            var result = new ClassType(new ID(env.CurrentClass.Name));
            node.ResolvedType = result;
            return result;
        }

        public DataType Visit(ID node, CompileEnviroment env)
        {
            DataType result;
            try
            {
                result = env.SymbolTable.Lookup(node.Name, node.Line, node.Column).Type;
            }
            catch (UndeclaredException)
            {
                if (env.CurrentClass != null &&
                    env.CurrentClass.Members.TryGetValue(node.Name, out var member))
                    result = member.Type;
                else
                    throw;
            }
            node.ResolvedType = result;
            return result;
        }

        public DataType Visit(IntType node, CompileEnviroment env)     => new IntType();
        public DataType Visit(FloatType node, CompileEnviroment env)   => new FloatType();
        public DataType Visit(BooleanType node, CompileEnviroment env) => new BooleanType();
        public DataType Visit(StringType node, CompileEnviroment env)  => new StringType();
        public DataType Visit(VoidType node, CompileEnviroment env)    => new VoidType();

        public DataType Visit(ClassType node, CompileEnviroment env)
        {
            env.SymbolTable.LookupClass(node.ClassName.Name, node.Line, node.Column);
            return node;
        }

        public DataType Visit(StaticAccessPrefixType node, CompileEnviroment env) => null;
    }
}
