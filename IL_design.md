# CPlus — Intermediate Language (IL) Design Document

This document describes the complete design of the CPlus Intermediate Language:
the data structures that represent it, every instruction in the instruction set,
how the stack works, and how each AST node maps to IL instructions.

---

## Table of Contents

1. [What Kind of IL](#1-what-kind-of-il)
2. [IL Data Structures](#2-il-data-structures)
3. [The Value Stack — How It Works](#3-the-value-stack--how-it-works)
4. [Instruction Set Reference](#4-instruction-set-reference)
   - 4.1 [Constant Loads](#41-constant-loads)
   - 4.2 [Local Variable Load / Store](#42-local-variable-load--store)
   - 4.3 [Parameter Load / Store](#43-parameter-load--store)
   - 4.4 [Field Load / Store](#44-field-load--store)
   - 4.5 [This Reference](#45-this-reference)
   - 4.6 [Object Creation](#46-object-creation)
   - 4.7 [Method Invocation](#47-method-invocation)
   - 4.8 [Arithmetic](#48-arithmetic)
   - 4.9 [Unary Operations](#49-unary-operations)
   - 4.10 [Comparison](#410-comparison)
   - 4.11 [Logical](#411-logical)
   - 4.12 [String Operations](#412-string-operations)
   - 4.13 [Type Conversion](#413-type-conversion)
   - 4.14 [Control Flow](#414-control-flow)
   - 4.15 [Stack Manipulation](#415-stack-manipulation)
   - 4.16 [Return](#416-return)
5. [AST → IL Translation Rules](#5-ast--il-translation-rules)
   - 5.1 [Program / Class / Field (structural)](#51-program--class--field-structural--no-instructions-emitted)
   - 5.2 [MethodDecl](#52-methoddecl)
   - 5.3 [Statements](#53-statements)
   - 5.4 [Expressions](#54-expressions)
   - 5.5 [Resolving an ID](#55-resolving-an-id)
6. [Full Worked Example](#6-full-worked-example)
7. [IL Generator Architecture](#7-il-generator-architecture)
8. [InvokeInstruction — The Tricky Part](#8-invokeinstruction--the-tricky-part)

---

## 1. What Kind of IL

CPlus IL is a **stack-based** intermediate language.

Instead of named registers (like in LLVM IR or x86 assembly), every operation reads its inputs from a virtual stack and writes its result back to that same stack. This is the same model used by the JVM and .NET CIL.

**Why stack-based for CPlus:**
- It is the simplest model to generate code for.
- Expressions in the AST are naturally recursive; recursion maps directly onto a stack (evaluate left subtree → stack, evaluate right subtree → stack, emit operator → pop two, push one).
- No register-allocation step is required.
- If you ever want to emit real .NET CIL or JVM bytecode, the model is identical and the translation is straightforward.

---

## 2. IL Data Structures

The IL representation of a whole program is a tree of plain data objects (no logic, just fields). Here is the full hierarchy:

```
ILProgram
└── List<ILClass> Classes

ILClass
├── string Name
├── List<ILField> Fields
└── List<ILMethod> Methods

ILField
├── string Name
├── DataType Type          (same DataType hierarchy used in the AST)
├── bool IsPublic
└── bool IsImmutable

ILMethod
├── string Name
├── DataType ReturnType
├── List<ILLocal> Params   (ordered; index 0, 1, 2 ... matched by position)
├── List<ILLocal> Locals   (ordered; index 0, 1, 2 ... separate from params)
└── List<ILInstruction> Instructions

ILLocal
├── string Name            (kept for debugging / pretty-printing)
├── DataType Type
└── int Index              (slot number; params and locals have independent
                            index spaces starting at 0)

ILInstruction              (base — see Section 4 for every concrete subtype)
├── Opcode Op
└── (subtype-specific fields — operand values)
```

**Notes:**
- `ILClass` / `ILField` / `ILMethod` / `ILLocal` are **structural** — they are built directly from AST nodes and do not produce instructions.
- Only `ILMethod.Instructions` contains the actual executable IL.
- Params and Locals have **separate index spaces**. `LOAD_ARG 0` is the first parameter; `LOAD_LOCAL 0` is the first local variable declared in the body. These are two different slots.

---

## 3. The Value Stack — How It Works

Every method has its own value stack. It starts empty when the method is called.

**Stack notation used throughout this document:**

```
[..., a, b] -> [..., result]
```

- The **rightmost** item is the **top** of the stack.
- `...` means "whatever was already there (we don't care)".
- `->` means "after the instruction executes".

**Rules:**
- Instructions that **read** values **pop** them from the stack.
- Instructions that **produce** values **push** them onto the stack.
- At a `RETURN` or `RETURN_VAL` instruction the stack must be empty (for void) or contain exactly one value (for non-void).
- Between statements the stack must always return to empty.

**Default values** (used when `NEW` allocates an object and initialises fields):

| Type      | Default |
|-----------|---------|
| `int`     | `0`     |
| `float`   | `0.0`   |
| `boolean` | `false` |
| `string`  | `""`    |
| class     | `null`  |

---

## 4. Instruction Set Reference

Each entry follows this format:

```
OPCODE_NAME   <operand type and meaning>
  Stack:  before -> after
  Notes:  ...
```

---

### 4.1 Constant Loads

Push a literal value onto the stack.

```
LOAD_CONST_INT   <int value>
  Stack:  [...] -> [..., int]
  Pushes the given integer literal value onto the stack.

LOAD_CONST_FLOAT   <float value>
  Stack:  [...] -> [..., float]
  Pushes the given float literal value onto the stack.

LOAD_CONST_BOOL   <bool value>   (true or false)
  Stack:  [...] -> [..., bool]
  Pushes the given boolean literal value onto the stack.

LOAD_CONST_STR   <string value>
  Stack:  [...] -> [..., string]
  Pushes the given string literal. Escape sequences (\n, \t, etc.) are already
  resolved by the lexer; the operand holds the final value, not the source text.

LOAD_NULL
  Stack:  [...] -> [..., null]
  Pushes a null reference. Used as the default for uninitialized class-type fields.
  No operand.
```

---

### 4.2 Local Variable Load / Store

Locals are the variables declared inside a method body (the `var_decl*` section before the statements). They are stored in slots indexed from 0.

```
LOAD_LOCAL   <int index>
  Stack:  [...] -> [..., value]
  Reads the value currently in local slot `index` and pushes it.
  Does not remove the value from the local slot.

STORE_LOCAL   <int index>
  Stack:  [..., value] -> [...]
  Pops the top value and writes it into local slot `index`.
```

---

### 4.3 Parameter Load / Store

Parameters are stored in their own index space, separate from locals, indexed from 0 in declaration order.

```
LOAD_ARG   <int index>
  Stack:  [...] -> [..., value]
  Reads the value currently in parameter slot `index` and pushes it.

STORE_ARG   <int index>
  Stack:  [..., value] -> [...]
  Pops the top value and writes it into parameter slot `index`.
  (Rarely needed in CPlus since parameters are not reassignable by language
  semantics, but the instruction exists for completeness.)
```

---

### 4.4 Field Load / Store

Instance fields are always accessed through an object reference. The object reference must be on the stack before the instruction executes.

```
LOAD_FIELD   <string fieldName>
  Stack:  [..., obj] -> [..., value]
  Pops the object reference `obj`, reads the field named `fieldName` from it,
  and pushes the value.
  Throws a null-reference error at runtime if obj is null.

STORE_FIELD   <string fieldName>
  Stack:  [..., obj, value] -> [...]
  Pops `value` first (top of stack), then pops `obj`, and writes `value` into
  the field named `fieldName` on `obj`.

  IMPORTANT: the order on the stack is obj BELOW value.
  To store a field: push the object reference FIRST, then the new value.
  Throws a null-reference error at runtime if obj is null.
```

---

### 4.5 This Reference

```
LOAD_THIS
  Stack:  [...] -> [..., this]
  Pushes the current object reference (the implicit receiver of every instance
  method). No operand.
  Used whenever you access a field or call a method on `this`.
```

---

### 4.6 Object Creation

```
NEW   <string className>
  Stack:  [...] -> [..., obj]
  Allocates a new heap object of the given class. All fields are set to their
  default values (see Section 3). Pushes the reference.
  Does NOT call any constructor; CPlus has no constructor syntax.
```

---

### 4.7 Method Invocation

Before either INVOKE instruction, the stack must look like:

```
[..., receiver, arg0, arg1, ..., argN-1]
```

Where:
- `receiver` is the object the method is called on — pushed **first**
- `arg0..N-1` are the arguments in **left-to-right** order — pushed after
- `N` is the `ArgCount` stored in the instruction

The runtime resolves the actual method using `ClassName + MethodName`. `ClassName` is the **statically resolved type** of the receiver (determined during semantic analysis), not the runtime type (CPlus has no inheritance / dynamic dispatch in the current design).

```
INVOKE   <ClassName> <MethodName> <ArgCount>
  Stack:  [..., receiver, arg0..argN-1] -> [..., returnValue]
  Calls the non-void method. Pops ArgCount arguments, then the receiver.
  Pushes the return value.
  Use this when the call result is used as an expression value.

INVOKE_VOID   <ClassName> <MethodName> <ArgCount>
  Stack:  [..., receiver, arg0..argN-1] -> [...]
  Calls a void method. Pops ArgCount arguments, then the receiver.
  Nothing is pushed because there is no return value.
  Use this for CallMethodStmt (the statement form of a method call).
```

**Why two separate opcodes instead of one?**
The semantic checker already distinguishes `CallExpression` (non-void) from `CallMethodStmt` (void). Carrying that distinction into the IL makes the interpreter/backend simpler: it never needs to inspect the return type at runtime to decide whether to push something.

---

### 4.8 Arithmetic

All binary arithmetic instructions pop two values (right first, then left) and push one result. Operands must be the same numeric type (int or float). If one side is int and the other float, emit `INT_TO_FLOAT` on the int side first (see [4.13](#413-type-conversion)).

```
ADD
  Stack:  [..., left, right] -> [..., left + right]
  Integer or float addition. No operand.

SUB
  Stack:  [..., left, right] -> [..., left - right]
  Integer or float subtraction. No operand.

MUL
  Stack:  [..., left, right] -> [..., left * right]
  Integer or float multiplication. No operand.

DIV
  Stack:  [..., left, right] -> [..., left / right]
  Integer or float division. Integer division truncates toward zero. No operand.
```

---

### 4.9 Unary Operations

```
NEG
  Stack:  [..., value] -> [..., -value]
  Arithmetic negation (unary minus). Operand must be int or float. No operand.

NOT
  Stack:  [..., bool] -> [..., !bool]
  Logical NOT. Operand must be bool. No operand.
```

---

### 4.10 Comparison

All comparison instructions pop two values and push a bool. Operands must be the same type.

```
EQ
  Stack:  [..., left, right] -> [..., left == right]
  Works on int, float, bool, string, and object references. No operand.

NEQ
  Stack:  [..., left, right] -> [..., left != right]
  No operand.

LT
  Stack:  [..., left, right] -> [..., left < right]
  Numeric types only. No operand.

LTE
  Stack:  [..., left, right] -> [..., left <= right]
  Numeric types only. No operand.

GT
  Stack:  [..., left, right] -> [..., left > right]
  Numeric types only. No operand.

GTE
  Stack:  [..., left, right] -> [..., left >= right]
  Numeric types only. No operand.
```

> `<`, `<=`, `>`, `>=` are included because `SematicCheckerAuto` already handles them even though they are currently commented out of the grammar. The IL is designed to not need changes when those operators are added.

---

### 4.11 Logical

At the IL level, `&&` and `||` are **not short-circuit** — they are simple binary ops that pop two bools and push one bool. If you want short-circuit evaluation, implement it using `JUMP_IF_FALSE` / `JUMP_IF_TRUE` instead (see [4.14](#414-control-flow)).

```
AND
  Stack:  [..., left, right] -> [..., left && right]
  Both operands must be bool. No operand.

OR
  Stack:  [..., left, right] -> [..., left || right]
  Both operands must be bool. No operand.
```

---

### 4.12 String Operations

```
CONCAT
  Stack:  [..., left, right] -> [..., left + right]
  String concatenation. Both operands must be string. No operand.
  (The '+' operator on strings maps to CONCAT, not ADD.)
```

---

### 4.13 Type Conversion

```
INT_TO_FLOAT
  Stack:  [..., int] -> [..., float]
  Widens an int to a float. Used to satisfy the rule that int is assignable to
  float. No operand.
  Emit this before any arithmetic or comparison instruction where one operand is
  int and the other is float.
```

---

### 4.14 Control Flow

Labels are string names that must be unique within a single method. The `LABEL` instruction itself does nothing at runtime — it is a marker.

```
LABEL   <string labelName>
  Stack:  unchanged.
  Pseudo-instruction. Marks a position in the instruction list that jump
  instructions can target. The interpreter skips it during sequential execution.

JUMP   <string labelName>
  Stack:  unchanged.
  Unconditionally transfers execution to the instruction after the LABEL with the
  given name.

JUMP_IF_TRUE   <string labelName>
  Stack:  [..., bool] -> [...]
  Pops a bool. If true, jumps to labelName. Otherwise falls through.

JUMP_IF_FALSE   <string labelName>
  Stack:  [..., bool] -> [...]
  Pops a bool. If false, jumps to labelName. Otherwise falls through.
```

**Control flow patterns:**

`if (cond) { body }`:
```
<emit cond>
JUMP_IF_FALSE  end_if
<emit body>
LABEL          end_if
```

`if (cond) { then } else { else_body }`:
```
<emit cond>
JUMP_IF_FALSE  else_label
<emit then_body>
JUMP           end_if
LABEL          else_label
<emit else_body>
LABEL          end_if
```

`while (cond) { body }`:
```
LABEL          loop_start
<emit cond>
JUMP_IF_FALSE  loop_end
<emit body>
JUMP           loop_start
LABEL          loop_end
```

> CPlus does not currently have `if`/`else` or `while` in its grammar, but the instructions are included so the IL does not need to change when those features are added.

**Label naming convention (suggested):** use a counter per method — `L0`, `L1`, `L2`, ... — or descriptive names like `if_end_12`, `while_start_5`.

---

### 4.15 Stack Manipulation

```
POP
  Stack:  [..., value] -> [...]
  Discards the top value. Used when a non-void INVOKE result is not needed
  (e.g. a method call used as a statement). No operand.

DUP
  Stack:  [..., value] -> [..., value, value]
  Duplicates the top value without removing it. Useful when you need to use a
  value and also keep it (e.g. store-and-keep patterns). No operand.
```

---

### 4.16 Return

```
RETURN
  Stack must be empty.
  Returns from a void method. No operand.

RETURN_VAL
  Stack:  [..., value] -> (exits method, value is the return value)
  Pops the top value and returns it to the caller. Used for all non-void methods.
  No operand.
```

---

## 5. AST → IL Translation Rules

The IL generator is a new visitor pass that runs **after** semantic analysis. It walks the AST and builds the `ILProgram` data structure.

---

### 5.1 Program / Class / Field (structural — no instructions emitted)

| AST Node    | Action |
|-------------|--------|
| `Program`   | Create `ILProgram`, recurse into each `ClassDecl` |
| `ClassDecl` | Create `ILClass(name)`, recurse into fields and methods |
| `FieldDecl` | Create `ILField(name, type, isPublic, isImmutable)` — fields hold state, not code |

---

### 5.2 MethodDecl

When entering a `MethodDecl`:

1. **Build param index table:**
   ```
   foreach (VarDecl p in method.Params) → paramIndex[p.Name.Name] = i++
   ```

2. **Build local index table:**
   ```
   foreach (StoreDecl d in method.Decls) → localIndex[d.Name.Name] = i++
   ```

3. **Emit initializers** — for each local declaration that has a `Value != null`:
   ```
   emit expression d.Value
   emit STORE_LOCAL localIndex[d.Name.Name]
   ```

4. **Emit each statement** in `method.Statements` (see [5.3](#53-statements))

5. **Implicit void return** — if the method is void and no explicit `Return` node is present, emit `RETURN` at the end.

**Context object to carry through the walk:**
```
string currentClassName         (set when entering ClassDecl)
Dictionary<string,int> paramIndex
Dictionary<string,int> localIndex
List<ILInstruction> instructions  (the list being built for this method)
```

---

### 5.3 Statements

**`Return` (void — `Expression` is null)**
```
emit:  RETURN
```

**`Return` (non-void — `Expression` is not null)**
```
emit:  <expression>
       RETURN_VAL
```

**`Assign` where LHS is `ID` (simple variable name)**
```
emit:  <expression>
then:
  name in paramIndex  → STORE_ARG  paramIndex[name]
  name in localIndex  → STORE_LOCAL localIndex[name]
  name is a field     → LOAD_THIS
                        <expression>   ← emit again
                        STORE_FIELD name
```
> **Note:** To store to a field you need `obj` on the stack **before** the value. The correct sequence for field assignment is always: `LOAD_THIS` → emit RHS expression → `STORE_FIELD name`.

**`Assign` where LHS is `FieldAccess` (`obj.field = expr`)**
```
emit:  <obj expression>     ← pushes receiver
       <value expression>   ← pushes new value
       STORE_FIELD fieldName
```

**`CallMethodStmt` (`obj.method(args)` as a void statement)**
```
emit:  <obj expression>
       <arg0 expression>
       <arg1 expression>
       ...
       INVOKE_VOID resolvedClassName methodName argCount
```
> `resolvedClassName` is the statically resolved type of `obj` — see [Section 8](#8-invokeinstruction--the-tricky-part).

---

### 5.4 Expressions

| AST Node | Instructions emitted |
|---|---|
| `IntLiteral(v)` | `LOAD_CONST_INT v` |
| `FloatLiteral(v)` | `LOAD_CONST_FLOAT v` |
| `BooleanLiteral(v)` | `LOAD_CONST_BOOL v` |
| `StringLiteral(v)` | `LOAD_CONST_STR v` |
| `ThisLiteral` | `LOAD_THIS` |
| `ID(name)` | see [5.5](#55-resolving-an-id) |
| `FieldAccess(obj, f)` | `<emit obj>` → `LOAD_FIELD f.Name` |
| `NewExpression(cls)` | `NEW cls.Name` |
| `UnaryOp("-", body)` | `<body>` → `NEG` |
| `UnaryOp("+", body)` | `<body>` (no-op; unary + is identity) |
| `UnaryOp("!", body)` | `<body>` → `NOT` |
| `CallExpression(obj, method, args)` | `<obj>` → `<arg0>` → ... → `INVOKE resolvedClass method.Name argCount` |

**`BinaryOp(op, L, R)`** — emit `<L>`, emit `<R>`, then the opcode below. If one side is `int` and the other `float`, emit `INT_TO_FLOAT` after the int operand before the arithmetic opcode.

| `op` | Opcode |
|------|--------|
| `+` on numbers | `ADD` |
| `+` on strings | `CONCAT` |
| `-` | `SUB` |
| `*` | `MUL` |
| `/` | `DIV` |
| `==` | `EQ` |
| `!=` | `NEQ` |
| `<` | `LT` |
| `<=` | `LTE` |
| `>` | `GT` |
| `>=` | `GTE` |
| `&&` | `AND` |
| `\|\|` | `OR` |

---

### 5.5 Resolving an ID

When you encounter `ID(name)`, look it up in this order:

1. `name` in `paramIndex`? → `LOAD_ARG paramIndex[name]`
2. `name` in `localIndex`? → `LOAD_LOCAL localIndex[name]`
3. `name` is a member of `currentClass` (a field)? → `LOAD_THIS` + `LOAD_FIELD name`

This mirrors exactly what `SematicCheckerAuto` does during its scope walk. If none of the three cases match, the semantic checker would have already thrown an `UndeclaredException`, so you can assert/throw here.

---

## 6. Full Worked Example

**Source:**
```
class Counter {
    private int count;

    public void reset() {
        count = 0;
    }

    public void increment(int amount) {
        count = count + amount;
    }

    public int get() {
        return count;
    }
}
```

---

**ILClass `Counter`**

Fields:
```
ILField { Name="count", Type=IntType, IsPublic=false, IsImmutable=false }
```

---

**ILMethod `reset`**
```
ReturnType: VoidType
Params:     (none)
Locals:     (none)

Instructions:
  LOAD_THIS
  LOAD_CONST_INT  0
  STORE_FIELD     "count"
  RETURN
```

Explanation of `count = 0`:
```
LOAD_THIS            push this          (obj for STORE_FIELD)
LOAD_CONST_INT 0     push 0             (value)
STORE_FIELD "count"  pop value, pop obj, write obj.count = 0
RETURN
```

---

**ILMethod `increment`**
```
ReturnType: VoidType
Params:     [ ILLocal { Name="amount", Type=IntType, Index=0 } ]
Locals:     (none)

Instructions:
  LOAD_THIS
  LOAD_THIS
  LOAD_FIELD   "count"
  LOAD_ARG     0
  ADD
  STORE_FIELD  "count"
  RETURN
```

Explanation of `count = count + amount`:
```
LOAD_THIS            push this              (for the final STORE_FIELD)
LOAD_THIS            push this              (receiver for LOAD_FIELD)
LOAD_FIELD "count"   pop this, push this.count
LOAD_ARG 0           push parameter `amount`
ADD                  pop amount, pop count, push count+amount
STORE_FIELD "count"  pop result, pop this, store
RETURN
```

---

**ILMethod `get`**
```
ReturnType: IntType
Params:     (none)
Locals:     (none)

Instructions:
  LOAD_THIS
  LOAD_FIELD   "count"
  RETURN_VAL
```

Explanation of `return count`:
```
LOAD_THIS            push this
LOAD_FIELD "count"   pop this, push this.count
RETURN_VAL           pop and return the int value
```

---

## 7. IL Generator Architecture

The IL generator is a new class that implements `IASTVisitor<T>`.

Because different `Visit` methods need to return different things (visiting a `MethodDecl` builds an `ILMethod`; visiting an expression emits instructions into an existing list), the simplest approach is a **side-effect visitor** — it mutates its own state rather than returning values:

```csharp
class ILGenerator : IASTVisitor<object?>
{
    // Output
    ILProgram program = new();

    // Per-class state
    ILClass currentILClass;
    string currentClassName;

    // Per-method state
    ILMethod currentILMethod;
    Dictionary<string, int> paramIndex = new();
    Dictionary<string, int> localIndex = new();

    // The working instruction list for the current method
    List<ILInstruction> instructions => currentILMethod.Instructions;

    // Counter for generating unique label names within a method
    int labelCounter = 0;
    string NewLabel() => $"L{labelCounter++}";

    // Resolved types — populated during SematicCheckerAuto (see Section 8)
    Dictionary<Expression, DataType> resolvedTypes;

    void Emit(ILInstruction instr) => instructions.Add(instr);
}
```

**Updated compiler pipeline:**

```
Parse Tree
  → CPlusASTVisitor      → AST
  → GetEnv               → CompileEnviroment (symbol table)
  → SematicCheckerAuto   → type-checks + annotates resolved types on expressions
  → ILGenerator          → ILProgram
  → (interpreter / backend)
```

---

## 8. InvokeInstruction — The Tricky Part

`INVOKE` and `INVOKE_VOID` both require the **static class name** of the receiver, e.g.:

```
INVOKE "Counter" "increment" 1
```

The AST node only gives you the receiver as an `Expression`. You need to know the *type* of that expression to find the class name. The type was computed by `SematicCheckerAuto` — but it is not stored anywhere right now.

**Solution: annotate the AST (or build a side table) during semantic analysis.**

**Option A — add a property to `Expression`** (simpler):
```csharp
// In CPlusAST.cs — add to the Expression class:
public DataType? ResolvedType { get; set; }

// In SematicCheckerAuto — wherever a Visit method returns a DataType,
// also set node.ResolvedType before returning.
```

**Option B — side table** (keeps the AST clean):
```csharp
// Pass a Dictionary<Expression, DataType> through the compiler.
// Fill it in SematicCheckerAuto. Read from it in ILGenerator.
```

Either approach works. Option A is simpler. Option B avoids touching the AST.

**Once you have the resolved type:**
- If the receiver's `ResolvedType` is `ClassType(className)` → `INVOKE className methodName argCount`
- If the receiver is `ThisLiteral` → the class is `currentClassName`
- The `ClassName` in the `InvokeInstruction` is the class that **owns** the method, not necessarily the class you are currently generating code in.
