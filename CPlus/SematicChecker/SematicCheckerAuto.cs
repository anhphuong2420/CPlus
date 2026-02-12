using Antlr4.Runtime.Misc;
using CPlus.Exceptions;
using CPlus.Exceptions.StaticErrors;
using CPlus.Helpers;
using CPlusAST;
using System;
using System.Linq;

namespace CPlus.SematicChecker
{
    public class SematicCheckerAuto : IASTVisitor<DataType>
    {
        public DataType Visit(CPlusAST.Program node, CompileEnviroment env)
        {
            var getEnvVisitor = new GetEnv();
            getEnvVisitor.Visit(node, env);
            // Handle class members
            foreach(var classDecl in node.ClassDecls)
            {
                classDecl.Accept(this, env);
            }
            return null;
        }

        public DataType Visit(ClassDecl node, CompileEnviroment env)
        {
            env.CurrentClass = env.SymbolTable.LookupClass(node.Name.Name, node.Line, node.Column);
            foreach (var mem in node.Members) { 
                mem.Accept(this, env);
            }
            env.CurrentClass = null;

            return null;
        }

        public DataType Visit(PublicModifier node, CompileEnviroment env)
        {
            return null;
        }

        public DataType Visit(PrivateModifier node, CompileEnviroment env)
        {
            return null;
        }

        public DataType Visit(FieldDecl node, CompileEnviroment env)
        {
            node.Decl.Accept(this, env);
            return null;
        }

        public DataType Visit(MethodDecl node, CompileEnviroment env)
        {
            if (node.ReturnType is ClassType classType)
            {
                env.SymbolTable.LookupClass(classType.ClassName.Name, node.Line, node.Column);
            }
            
            env.CurrentMethod = node;
            env.SymbolTable.EnterScope();
            
            foreach(var par in node.Params)
            {
                // Check parameter type validity
                if (par.DataType is ClassType paramClassType)
                {
                    env.SymbolTable.LookupClass(paramClassType.ClassName.Name, par.Line, par.Column);
                }
                env.SymbolTable.AddParamSymbol(new Symbol(par.Name.Name, par.DataType), par.Line, par.Column);
            }
            
            foreach(var decl in node.Decls)
            {
                decl.Accept(this, env);
            }

            // Expect for non-void, method must return
            if (node.ReturnType is not VoidType)
            {
                var hasReturnInAllPaths = false;
                foreach (var stmt in node.Statements)
                {
                    if (CheckerHelper.HasReturnInAllPaths(stmt))
                    {
                        hasReturnInAllPaths = true;
                        break;
                    }
                }
                if (!hasReturnInAllPaths)
                {
                    throw new TypeMismatchInStatementException($"Method {node.Name.Name} must return a value", node.ReturnType?.ToString(), "None", node.Line, node.Column);
                }
            }

            foreach (var stmt in node.Statements)
            {
                stmt.Accept(this, env);
            }
            
            env.SymbolTable.ExitScope();
            env.CurrentMethod = null;
            return null;
        }

        public DataType Visit(VarDecl node, CompileEnviroment env)
        {
            if (node.DataType is ClassType classType)
            {
                env.SymbolTable.LookupClass(classType.ClassName.Name, node.Line, node.Column);
            }
            
            // Register symbol if inside a method (local variable)
            // FieldDecls are already handled by GetEnv, but VarDecl visitor might be called for fields too?
            // In Visit(FieldDecl), it calls node.Decl.Accept(this, env). node.Decl is StoreDecl (VarDecl or ConstDecl).
            // So we need to be careful not to double add if it was already added to class members.
            // But GetEnv adds to ClassSymbol.Members. Here we add to SymbolTable (scopes).
            // CompileEnviroment.SymbolTable logic needs to be respected.
            
            if (env.CurrentMethod != null)
            {
                env.SymbolTable.AddSymbol(new Symbol(node.Name.Name, node.DataType), node.Line, node.Column);
            }

            if (node.Value != null)
            {
                var valType = node.Value.Accept(this, env);
                if (!CheckerHelper.CanAssign(node.DataType, valType, env))
                {
                     throw new TypeMismatchInStatementException("Variable Declaration", valType.ToString(), node.DataType.ToString(), node.Line, node.Column);
                }
            }
            return null;
        }

        public DataType Visit(ConstDecl node, CompileEnviroment env)
        {
            if (node.DataType is ClassType classType)
            {
                env.SymbolTable.LookupClass(classType.ClassName.Name, node.Line, node.Column);
            }

            if (env.CurrentMethod != null) { 
                env.SymbolTable.AddSymbol(new Symbol(node.Name.Name, node.DataType, isImmutable: true), node.Line, node.Column);
            }
            
            if (!CheckerHelper.IsConstantExpression(node.Value, env))
            {
                throw new IllegalConstantExpressionException(node.Name.Name, node.Value?.ToString(), node.Line, node.Column);
            }
            
            if (node.Value != null)
            {
                 var valType = node.Value.Accept(this, env);
                 if (!CheckerHelper.CanAssign(node.DataType, valType, env))
                 {
                      throw new TypeMismatchInStatementException("Constant Declaration", valType.ToString(), node.DataType.ToString(), node.Line, node.Column);
                 }
            }
            
            return null;
        }

        public DataType Visit(Assign node, CompileEnviroment env)
        {
             var lhsType = node.LHS.Accept(this, env);
             var rhsType = node.Expression.Accept(this, env);

             // Check for immutability
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
                     {
                         symbol = member;
                     }
                     else
                     {
                         throw;
                     }
                 }

                 if (symbol.IsImmutable)
                 {
                     throw new CannotAssignToConstantException(id.Name, node.Line, node.Column);
                 }
             } 
             // Logic for FieldAccess immutability check is in FieldAccess visit or here?
             // Since fieldAccess returns a type, we might need to check symbol properties manually if we want to catch const assignment.
             // But for now, let's just check types.
             
             if (!CheckerHelper.CanAssign(lhsType, rhsType, env))
             {
                 throw new TypeMismatchInStatementException("Assignment", rhsType.ToString(), lhsType.ToString(), node.Line, node.Column);
             }
             return null;
        }

        public DataType Visit(Return node, CompileEnviroment env)
        {
            var returnType = env.CurrentMethod.ReturnType;
            if (env.CurrentMethod.ReturnType is VoidType && node.Expression != null)
            {
                throw new TypeMismatchInStatementException("Return", "Not-Void", "Void", node.Line, node.Column);
            }
            else if (env.CurrentMethod.ReturnType is VoidType && node.Expression == null)
            {
                return null;
            }
            
            // If we are here, returnType is NOT VoidType, so we expect an expression
            if (node.Expression == null)
            {
                 // Allowed in some languages to just 'return;' in non-void function? 
                 // Usually means return default value or error.
                 // The earlier check `if (node.ReturnType is not VoidType)` ensures we have returns in all paths.
                 // But a specific `return;` statement in a non-void function is usually an error.
                 throw new TypeMismatchInStatementException("Return", "Void", returnType.ToString(), node.Line, node.Column);
            }

            var expectedType = node.Expression.Accept(this, env);
            if(!CheckerHelper.CanAssign(returnType, expectedType, env))
            {
                throw new TypeMismatchInStatementException("Return", expectedType.ToString(), returnType.ToString(), node.Line, node.Column);
            }
            return null;
        }

        public DataType Visit(CallMethodStmt node, CompileEnviroment env)
        {
            // Logic reused for CallExpression mostly, but strict void check?
            DataType classExprType = null;
            ClassSymbol classSymbol = null;

            if (node.Obj is ID id)
            {
                if (env.SymbolTable.Lookup(id.Name, id.Line, id.Column) is Symbol s)
                {
                    classExprType = s.Type;
                }
            }
            else
            {
                classExprType = node.Obj.Accept(this, env);
            }

            if (classExprType is not ClassType cType)
            {
                throw new TypeMismatchInStatementException("Method call", "Class type", classExprType?.ToString() ?? "null", node.Line, node.Column);
            }
            
            classSymbol = env.SymbolTable.LookupClass(cType.ClassName.Name, node.Line, node.Column);
            
            // Find method
            var methodSymbol = classSymbol.Members.Values.OfType<MethodSymbol>().FirstOrDefault(m => m.Name == node.Method.Name);

            if (methodSymbol == null)
            {
                throw new UndeclaredException(new Method(), node.Method.Name, node.Line, node.Column);
            }
            
            if (!methodSymbol.IsPublic)
            {
                 // Check if we are inside the same class
                 if (env.CurrentClass.Name != classSymbol.Name)
                 {
                     throw new IllegalMemberAccessException(new Method(), node.Method.Name, classSymbol.Name, node.Line, node.Column);
                 }
            }

            // Check parameters
            if (node.Params.Count() != methodSymbol.Parameters.Count())
            {
                throw new TypeMismatchInStatementException($"Method {node.Method.Name}", $"{methodSymbol.Parameters.Count()} arguments", $"{node.Params.Count()} arguments", node.Line, node.Column);
            }

            var paramDecls = methodSymbol.Parameters.ToList();
            var args = node.Params.ToList();

            for(int i=0; i<args.Count; i++)
            {
                var argType = args[i].Accept(this, env);
                var paramType = paramDecls[i].DataType;
                if (!CheckerHelper.CanAssign(paramType, argType, env))
                {
                    throw new TypeMismatchInStatementException($"Argument {i+1} of {node.Method.Name}", argType.ToString(), paramType.ToString(), node.Line, node.Column);
                }
            }
            
            // In strict semantic checker, CallMethodStmt means we are ignoring the return value.
            // Some languages allow ignoring return value of non-void methods.
            // The original code enforced: if (methodSymbol.Type is not VoidType) throw...
            // This force users to use 'CallExpression' logic for non-void?
            // Or maybe CPlus requires methods called as statements to return void?
            // "TypeMismatchInStatementException: Method call, Class type...".
            // Let's implement the existing behavior: enforce VoidType.
            
            if (methodSymbol.Type is not VoidType)
            {
                 // This seems to restrict "void methods can be statements".
                 // "Non-void methods must be used in expression"?
                 // Let's stick effectively to functionality. If the user code has `a.b();` and `b` returns int, this throws.
                 // This is a language design choice.
                 throw new TypeMismatchInStatementException("Method call statement", "Void", methodSymbol.Type.ToString(), node.Line, node.Column);
            }

            return null;
        }

        public DataType Visit(BinaryOp node, CompileEnviroment env)
        {
            var left = node.Left.Accept(this, env);
            var right = node.Right.Accept(this, env);

            if (node.Op == "&&" || node.Op == "||")
            {
                if (!(left is BooleanType) || !(right is BooleanType))
                    throw new TypeMismatchInExpressionException(node.Op, "Boolean", $"{left},{right}", node.Line, node.Column);
                return new BooleanType();
            }
            
            if (new[] { "+", "-", "*", "/", "%" }.Contains(node.Op))
            {
                if (CheckerHelper.CanAssign(new FloatType(), left, env) && CheckerHelper.CanAssign(new FloatType(), right, env))
                {
                    if (left is FloatType || right is FloatType) return new FloatType();
                    return new IntType();
                }
                throw new TypeMismatchInExpressionException(node.Op, "Int/Float", $"{left},{right}", node.Line, node.Column);
            }
            
            if (new[] { ">", "<", ">=", "<=" }.Contains(node.Op))
            {
                 if (CheckerHelper.CanAssign(new FloatType(), left, env) && CheckerHelper.CanAssign(new FloatType(), right, env))
                {
                    return new BooleanType();
                }
                throw new TypeMismatchInExpressionException(node.Op, "Int/Float", $"{left},{right}", node.Line, node.Column);
            }

            if (new[] { "==", "!=" }.Contains(node.Op))
            {
                // Can compare primitive types and classes (reference equality?)
                // Assuming basic types for now
                 if (CheckerHelper.CanAssign(left, right, env) || CheckerHelper.CanAssign(right, left, env))
                {
                    return new BooleanType();
                }
                 throw new TypeMismatchInExpressionException(node.Op, "Compatible Types", $"{left},{right}", node.Line, node.Column);
            }

            return left;
        }

        public DataType Visit(UnaryOp node, CompileEnviroment env)
        {
             var bodyType = node.Body.Accept(this, env);
             if (node.Op == "!")
             {
                 if (bodyType is not BooleanType)
                    throw new TypeMismatchInExpressionException(node.Op, "Boolean", bodyType.ToString(), node.Line, node.Column);
                 return new BooleanType();
             }
             if (node.Op == "-")
             {
                 if (bodyType is IntType) return new IntType();
                 if (bodyType is FloatType) return new FloatType();
                 throw new TypeMismatchInExpressionException(node.Op, "Int/Float", bodyType.ToString(), node.Line, node.Column);
             }
             return bodyType;
        }

        public DataType Visit(CallExpression node, CompileEnviroment env)
        {
            DataType classExprType = null;
            
            if (node.Obj is ID id)
            {
                 var symbol = env.SymbolTable.Lookup(id.Name, id.Line, id.Column);
                 classExprType = symbol.Type;
            }
            else
            {
                classExprType = node.Obj.Accept(this, env);
            }

            if (classExprType is not ClassType cType)
            {
                throw new TypeMismatchInExpressionException("Method Call", "ClassType", classExprType.ToString(), node.Line, node.Column);
            }
            
            var classSymbol = env.SymbolTable.LookupClass(cType.ClassName.Name, node.Line, node.Column);
            var methodSymbol = classSymbol.Members.Values.OfType<MethodSymbol>().FirstOrDefault(m => m.Name == node.Method.Name);
            
            if (methodSymbol == null)
            {
                throw new UndeclaredException(new Method(), node.Method.Name, node.Line, node.Column);
            }

             if (!methodSymbol.IsPublic)
            {
                 // Check if we are inside the same class
                 if (env.CurrentClass.Name != classSymbol.Name)
                 {
                     throw new IllegalMemberAccessException(new Method(), node.Method.Name, classSymbol.Name, node.Line, node.Column);
                 }
            }

            // Check args
             if (node.Params.Count() != methodSymbol.Parameters.Count())
            {
                throw new TypeMismatchInExpressionException($"Method {node.Method.Name}", $"{methodSymbol.Parameters.Count()} arguments", $"{node.Params.Count()} arguments", node.Line, node.Column);
            }

             var paramDecls = methodSymbol.Parameters.ToList();
            var args = node.Params.ToList();

            for(int i=0; i<args.Count; i++)
            {
                var argType = args[i].Accept(this, env);
                var paramType = paramDecls[i].DataType;
                if (!CheckerHelper.CanAssign(paramType, argType, env))
                {
                     throw new TypeMismatchInExpressionException($"Argument {i+1} of {node.Method.Name}", paramType.ToString(), argType.ToString(), node.Line, node.Column);
                }
            }
            
            if (methodSymbol.Type is VoidType)
            {
                throw new TypeMismatchInExpressionException("Method Call", "Non-Void", "Void", node.Line, node.Column);
            }

            return methodSymbol.Type;
        }

        public DataType Visit(NewExpression node, CompileEnviroment env)
        {
            env.SymbolTable.LookupClass(node.ClassName.Name, node.Line, node.Column);
            return new ClassType(node.ClassName);
        }

        public DataType Visit(FieldAccess node, CompileEnviroment env)
        {
             DataType objType = null;
             if (node.Obj is ID id)
             {
                 objType = env.SymbolTable.Lookup(id.Name, id.Line, id.Column).Type;
             }
             else
             {
                 objType = node.Obj.Accept(this, env);
             }
             
             if (objType is not ClassType cType)
             {
                 throw new TypeMismatchInExpressionException("Field Access", "ClassType", objType.ToString(), node.Line, node.Column);
             }
             
             var classSymbol = env.SymbolTable.LookupClass(cType.ClassName.Name, node.Line, node.Column);
             if (!classSymbol.Members.TryGetValue(node.FieldName.Name, out var memberSymbol))
             {
                  throw new UndeclaredException(new Variable(), node.FieldName.Name, node.Line, node.Column);
             }
             
             // Check visibility
             if (!memberSymbol.IsPublic && env.CurrentClass.Name != classSymbol.Name)
             {
                 throw new IllegalMemberAccessException(new Variable(), node.FieldName.Name, classSymbol.Name, node.Line, node.Column);
             }
             
             return memberSymbol.Type;
        }

        public DataType Visit(IntLiteral node, CompileEnviroment env)
        {
            return new IntType();
        }

        public DataType Visit(FloatLiteral node, CompileEnviroment env)
        {
            return new FloatType();
        }

        public DataType Visit(StringLiteral node, CompileEnviroment env)
        {
            return new StringType();
        }

        public DataType Visit(BooleanLiteral node, CompileEnviroment env)
        {
            return new BooleanType();
        }

        public DataType Visit(ThisLiteral node, CompileEnviroment env)
        {
             if (env.CurrentClass == null)
            {
                // Should not happen if grammar enforces 'this' usage
                // But generally 'this' is only valid inside class methods (which are all methods here)
            }
            return new ClassType(new ID(env.CurrentClass.Name));
        }

        public DataType Visit(ID node, CompileEnviroment env)
        {
            try
            {
                return env.SymbolTable.Lookup(node.Name, node.Line, node.Column).Type;
            }
            catch (UndeclaredException)
            {
                if (env.CurrentClass != null && env.CurrentClass.Members.TryGetValue(node.Name, out var member))
                {
                    return member.Type;
                }
                throw;
            }
        }

        public DataType Visit(IntType node, CompileEnviroment env)
        {
            return new IntType();
        }

        public DataType Visit(FloatType node, CompileEnviroment env)
        {
            return new FloatType();
        }

        public DataType Visit(BooleanType node, CompileEnviroment env)
        {
            return new BooleanType();
        }

        public DataType Visit(StringType node, CompileEnviroment env)
        {
            return new StringType();
        }

        public DataType Visit(VoidType node, CompileEnviroment env)
        {
            return new VoidType();
        }

        public DataType Visit(ClassType node, CompileEnviroment env)
        {
            env.SymbolTable.LookupClass(node.ClassName.Name, node.Line, node.Column);
            return node;
        }

        public DataType Visit(StaticAccessPrefixType node, CompileEnviroment env)
        {
            // Not implemented fully in original? Unused?
            return null;
        }
    }
}
