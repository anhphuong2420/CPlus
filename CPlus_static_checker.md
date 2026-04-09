# CPlus Static Checker — Exceptions and Strategies

## Overview

The static checker runs in two sequential passes over the AST, using a shared `CompileEnviroment`:

```
Parse → AST
           → Pass 1: GetEnv              (builds symbol tables)
           → Pass 2: SematicCheckerAuto  (type checking, control flow, immutability)
```

Both passes implement `IASTVisitor<T>`. Every static error is a subclass of `CplusStaticException`.

---

## Pass 1 — GetEnv

`GetEnv` is a pre-pass that does a single structural walk, only visiting `ClassDecl`, `FieldDecl`, and `MethodDecl`. All other visitor methods throw `NotImplementedException`.

**What it does:**
1. **First loop** — registers every class name in the symbol table so forward references between classes are possible.
2. **Second loop** — enters each class and registers its fields and methods as `Symbol`/`MethodSymbol` entries in `ClassSymbol.Members`.

**Errors thrown by GetEnv:**

| Exception | Trigger |
|-----------|---------|
| `RedeclaredException(Attribute, ...)` | A field name already exists in `ClassSymbol.Members` |
| `RedeclaredException(Method, ...)` | A method name already exists in `ClassSymbol.Members` |
| `RedeclaredException(Class, ...)` | A class name is declared more than once (thrown inside `SymbolTable.AddClass`) |

After GetEnv completes, the symbol table contains all class shapes but no local scopes — those are built during Pass 2.

---

## Pass 2 — SematicCheckerAuto

`SematicCheckerAuto` implements `IASTVisitor<DataType>`. Every `Visit` for an expression returns that expression's resolved `DataType`, which the caller uses for further checking. It also writes `node.ResolvedType` on every `Expression` node, which the IL generator reads later.

### Environment State

| Field | Purpose |
|-------|---------|
| `SymbolTable` | Stack of scopes (local variables, parameters) plus flat map of classes |
| `CurrentClass` | The `ClassSymbol` being visited — used to resolve `this` and unqualified field names |
| `CurrentMethod` | The `MethodDecl` being visited — used to validate `return` types |

`EnterScope`/`ExitScope` is called around each method body. Parameters are added to the current scope with `AddParamSymbol`; local variable declarations are added with `AddSymbol`.

---

## CheckerHelper Utilities

Three static helpers are used throughout Pass 2:

### `CanAssign(fromType, toType, env) → bool`

The type-compatibility predicate. Implements the assignment rule:
- Same type is always assignable to itself.
- `int` is assignable to `float` (widening coercion — `int` is a subtype of `float` in CPlus).
- `ClassType` is only assignable if both sides name the same class (no inheritance).
- All other cross-type combinations return `false`.

### `IsConstantExpression(node, env) → bool`

Used exclusively for `immut` (constant) declarations. Returns `true` if the expression can be evaluated at compile time:
- Any `Literal` node (`IntLiteral`, `FloatLiteral`, etc.) → `true`.
- `BinaryOp` → both operands must be constant.
- `UnaryOp` → body must be constant.
- `ID` → must resolve to an `immut` symbol.
- `FieldAccess` where the object is an `ID` → the accessed field must be `immut`.
- Everything else (including `ThisLiteral`) → `false`.

### `HasReturnInAllPaths(node) → bool`

Used for return-completeness checking. Currently only returns `true` for a bare `Return` statement. A `TODO` in the code notes this will need to be expanded for `if/else` and loops.

---

## All Static Exceptions

### 1. `RedeclaredException`

**Message:** `Redeclared {Kind}: '{name}' at line L, column C`

**Kind values:** `Class`, `Method`, `Attribute`, `Variable`, `Constant`, `Parameter`

| Where thrown | Condition |
|---|---|
| `GetEnv.Visit(FieldDecl)` | Field name already in `ClassSymbol.Members` |
| `GetEnv.Visit(MethodDecl)` | Method name already in `ClassSymbol.Members` |
| `SymbolTable.AddClass` | Class name already registered in the class map |
| `SymbolTable.AddSymbol` | Local variable/constant name already in the current scope |
| `SymbolTable.AddParamSymbol` | Parameter name already in the current scope |

---

### 2. `UndeclaredException`

**Message:** `Undeclared {Kind}: '{name}' at line L, column C`

**Kind values:** `Variable`, `Class`, `Method`

| Where thrown | Condition |
|---|---|
| `SymbolTable.Lookup` | Name not found in any active scope |
| `SymbolTable.LookupClass` | Class name not in the class map |
| `Visit(CallMethodStmt)` | Method name not found in the resolved class's members |
| `Visit(CallExpression)` | Same |
| `Visit(FieldAccess)` | Field name not found in the resolved class's members |

**Lookup resolution order for bare identifiers:** Pass 2 first tries `SymbolTable.Lookup` (local scopes + params). If that throws `UndeclaredException`, it falls back to `CurrentClass.Members` (implicit `this` field access). If neither succeeds, the exception propagates.

---

### 3. `TypeMismatchInStatementException`

**Message:** `Type mismatch in {statementType} statement at line L, column C: expected {expected}, got {actual}`

| Where thrown | `statementType` | Condition |
|---|---|---|
| `Visit(VarDecl)` | `"Variable Declaration"` | Initializer type not assignable to declared type |
| `Visit(ConstDecl)` | `"Constant Declaration"` | Initializer type not assignable to declared type |
| `Visit(Assign)` | `"Assignment"` | RHS type not assignable to LHS type |
| `Visit(CallMethodStmt)` | `"Method call"` | Receiver expression is not a `ClassType` |
| `Visit(CallMethodStmt)` | `"Argument N of method"` | Argument type not assignable to parameter type |
| `Visit(CallMethodStmt)` | `"Method call statement"` | Calling a non-void method as a statement (result discarded) |

---

### 4. `TypeMismatchInExpressionException`

**Message:** `Type mismatch in expression with operator '{op}' at line L, column C: expected {expected}, got {actual}`

| Where thrown | `operator` | Condition |
|---|---|---|
| `Visit(BinaryOp)` | `"&&"` / `"||"` | Either operand is not `boolean` |
| `Visit(BinaryOp)` | `"+"`, `"-"`, `"*"`, `"/"`, `"%"` | Either operand is not `int`/`float` |
| `Visit(BinaryOp)` | `">"`, `"<"`, `">="`, `"<="` | Either operand is not `int`/`float` |
| `Visit(BinaryOp)` | `"=="`, `"!="` | Neither side is assignable to the other |
| `Visit(UnaryOp)` | `"!"` | Operand is not `boolean` |
| `Visit(UnaryOp)` | `"-"` | Operand is not `int`/`float` |
| `Visit(CallExpression)` | `"Method Call"` | Receiver expression is not a `ClassType` |
| `Visit(CallExpression)` | `"Argument N of method"` | Argument type not assignable to parameter type |
| `Visit(CallExpression)` | `"Method Call"` | Calling a void method in an expression context |
| `Visit(FieldAccess)` | `"Field Access"` | Object expression is not a `ClassType` |

---

### 5. `CannotAssignToConstantException`

**Message:** `Cannot assign to constant '{name}' at line L, column C`

**Where thrown:** `Visit(Assign)` — after resolving the LHS identifier, if `symbol.IsImmutable` is `true`.

**Strategy:** The LHS is resolved the same way as a normal `ID` lookup (scope → class members fallback). The immutability flag on the `Symbol` is then checked.

---

### 6. `IllegalConstantExpressionException`

**Message:** `Illegal constant expression for '{name}' at line L, column C: expected literal, got '{expr}'`

**Where thrown:** `Visit(ConstDecl)` — if `CheckerHelper.IsConstantExpression(node.Value, env)` returns `false`.

**Strategy:** `IsConstantExpression` performs a recursive structural check on the initializer expression. Only literal nodes, `immut` identifiers, and pure compositions of those (via `BinaryOp`/`UnaryOp`) pass.

---

### 7. `UninitializedImmutableException`

**Message:** `Immutable variable '{name}' must have an initializer (line L, column C)`

**Where thrown:** `Visit(ConstDecl)` — if `node.Value == null`.

**Strategy:** Checked before `IsConstantExpression`. An `immut` declaration with no `=` expression is unconditionally rejected.

---

### 8. `IllegalMemberAccessException`

**Message:** `Illegal access to private {Kind} '{member}' in class '{class}' at line L, column C`

| Where thrown | Condition |
|---|---|
| `Visit(CallMethodStmt)` | `!methodSymbol.IsPublic` and `CurrentClass.Name != classSymbol.Name` |
| `Visit(CallExpression)` | Same |
| `Visit(FieldAccess)` | `!memberSymbol.IsPublic` and `CurrentClass.Name != classSymbol.Name` |

**Strategy:** After resolving the member, if it is private and the call site is outside the owning class, access is denied. Calls from within the same class always succeed regardless of visibility.

---

### 9. `ArgumentCountMismatchException`

**Message:** `Method '{name}' expects {expected} argument(s) but got {actual} (line L, column C)`

**Where thrown:** `Visit(CallMethodStmt)` and `Visit(CallExpression)` — after resolving the method symbol, if `node.Params.Count() != methodSymbol.Parameters.Count()`.

**Strategy:** Arity is checked before per-argument type checking, so you always get a clear "wrong count" error rather than a confusing type error on a misaligned argument.

---

### 10. `MissingReturnException`

**Message:** `Method '{name}' must return a value on all code paths (line L, column C)`

**Where thrown:** `Visit(MethodDecl)` — for non-void methods, if `node.Statements.Any(HasReturnInAllPaths)` is `false`.

**Strategy:** `HasReturnInAllPaths` currently only recognises a bare `Return` node as covering all paths. This is conservative: any method body without a top-level `return` statement fails. The check runs on the statement list before any statement is visited.

---

### 11. `UnreachableStatementException`

**Message:** `Unreachable statement at line L, column C`

**Where thrown:** `Visit(MethodDecl)` — while iterating statements, if `returnSeen` is `true` when the next statement begins.

**Strategy:** A boolean flag `returnSeen` is set to `true` after any statement for which `HasReturnInAllPaths` returns `true`. The following statement immediately triggers the exception. This catches code after an unconditional `return`.

---

### 12. `VoidReturnValueException`

**Message:** `Void method '{name}' cannot return a value (line L, column C)`

**Where thrown:** `Visit(Return)` — if `CurrentMethod.ReturnType` is `VoidType` and `node.Expression != null`.

---

### 13. `ReturnTypeMismatchException`

**Message:** `Method '{name}' must return '{expected}' but got '{actual}' (line L, column C)`

| Condition | `actual` |
|---|---|
| Non-void method contains a bare `return;` | `"void"` |
| `return expr;` where expr type is not assignable to declared return type | the expression type |

**Where thrown:** `Visit(Return)`.

---

### 14. `MissingMainException`

**Message:** `No entry point found. Define 'public void main()' in one of your classes.`

**Where thrown:** `CheckEntryPoint` (called at the end of `Visit(Program)`) — if no class contains any member named `"main"` that is a `MethodSymbol`.

---

### 15. `InvalidMainSignatureException`

**Message:** `Invalid entry point in class '{cls}': found '{actualSig}' but expected 'public void main()' (line L, column C)`

**Where thrown:** `CheckEntryPoint` — if a class has a member named `"main"` that is a `MethodSymbol`, but it is private, has a non-void return type, or takes parameters.

**Strategy:** `CheckEntryPoint` scans all classes and stops at the first `main` method it finds. If that method's signature is wrong it throws; if it is correct it returns immediately. Only if no class has any `main` at all does `MissingMainException` fire.

---

## Pass 2 Type Resolution Summary

Every expression visitor writes `node.ResolvedType` before returning. This field is later consumed by the IL generator. The resolved types for each expression form:

| Expression | Resolved type |
|---|---|
| `IntLiteral` | `IntType` |
| `FloatLiteral` | `FloatType` |
| `StringLiteral` | `StringType` |
| `BooleanLiteral` | `BooleanType` |
| `ThisLiteral` | `ClassType(CurrentClass.Name)` |
| `ID` | Type from symbol lookup |
| `FieldAccess` | Type of the resolved field symbol |
| `NewExpression` | `ClassType(node.ClassName)` |
| `CallExpression` | Method's declared return type |
| `BinaryOp` (`+`,`-`,etc.) | `IntType` or `FloatType` depending on operands |
| `BinaryOp` (comparison/logical) | `BooleanType` |
| `UnaryOp(!)` | `BooleanType` |
| `UnaryOp(-)` | Same as operand (`int` or `float`) |
