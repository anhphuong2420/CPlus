# CPlus IL — Design, .NET Mapping, and ilasm Generation

## Table of Contents

1. [How .NET CIL Works](#1-how-net-cil-works)
   - 1.1 [The Big Picture — From Source to Execution](#11-the-big-picture--from-source-to-execution)
   - 1.2 [The Evaluation Stack](#12-the-evaluation-stack)
   - 1.3 [Method Frames](#13-method-frames)
   - 1.4 [The CLR Type System in CIL](#14-the-clr-type-system-in-cil)
   - 1.5 [The Verifier and .maxstack](#15-the-verifier-and-maxstack)
   - 1.6 [The .il File Structure](#16-the-il-file-structure)
   - 1.7 [The CIL Argument Convention](#17-the-cil-argument-convention)
2. [CPlus IL Data Model](#2-cplus-il-data-model)
3. [The CPlus Value Stack](#3-the-cplus-value-stack)
4. [CPlus Instruction Set Reference](#4-cplus-instruction-set-reference)
5. [AST → IL Generation Strategies](#5-ast--il-generation-strategies)
6. [ILProgram → CIL Mapping (Complete)](#6-ilprogram--cil-mapping-complete)
   - 6.1 [Type Name Mapping](#61-type-name-mapping)
   - 6.2 [Instruction-by-Instruction CIL Output](#62-instruction-by-instruction-cil-output)
   - 6.3 [Multi-Instruction Expansions Explained](#63-multi-instruction-expansions-explained)
   - 6.4 [MaxStack Computation Algorithm](#64-maxstack-computation-algorithm)
     - 6.4.1 [The Core Loop](#641-the-core-loop)
     - 6.4.2 [The Problem — Multi-Instruction Expansions](#642-the-problem--multi-instruction-expansions)
     - 6.4.3 [The Fix — Intermediate Peak Check](#643-the-fix--intermediate-peak-check)
     - 6.4.4 [Stack Deltas Reference](#644-stack-deltas-reference)
     - 6.4.5 [Worked Examples](#645-worked-examples)
   - 6.5 [ILMethod.Dump()](#65-ilmethoddump)
   - 6.6 [ILClass.Dump()](#66-ilclassdump)
   - 6.7 [ILProgram.Dump()](#67-ilprogramdump)
7. [Full Worked Example](#7-full-worked-example)

---

## 1. How .NET CIL Works

### 1.1 The Big Picture — From Source to Execution

When you write C# (or any .NET language), it does not compile directly to native machine code. Instead the compiler produces a **managed executable** (.exe or .dll) containing CIL bytecode. The CLR then executes that bytecode.

```
C# / CPlus source
      ↓  compiler (csc, ilasm, etc.)
CIL bytecode  (inside .exe / .dll)
      ↓  CLR loads the assembly
      ↓  JIT compiler  (just before first call to each method)
Native machine code  (x64, ARM, etc.)
      ↓  CPU executes
```

**Why the two-step process?**

- The CIL bytecode is **platform-neutral**. The same `.exe` runs on Windows x64, Linux ARM64, etc.
- The **JIT** (Just-In-Time compiler) converts each method from CIL to native code the first time it is called. After that, the native version is cached and called directly — no interpretation overhead.
- The CLR also manages memory (garbage collection), enforces type safety, and catches invalid operations (null dereference, array out-of-bounds) before they corrupt memory.

**`ilasm`** is a low-level tool that takes a human-readable `.il` text file and assembles it directly into a `.exe`/`.dll` without going through a high-level language compiler. This is what CPlus targets.

---

### 1.2 The Evaluation Stack

CIL is a **stack-based** bytecode language. Every method has its own private **evaluation stack** — a LIFO structure that holds intermediate values during computation.

**The evaluation stack is not the call stack.** These are two completely different things:

| | Evaluation stack | Call stack |
|---|---|---|
| What it holds | Intermediate computed values (ints, floats, object references) | Method call frames (return addresses, local variable storage) |
| Grows when | An instruction pushes a value | A method is called |
| Shrinks when | An instruction pops a value | A method returns |
| Scope | Local to one method execution | Spans the whole call chain |

**How instructions use it:**

Every CIL instruction either reads from or writes to the evaluation stack (or both):

```
                  Stack before   Instruction    Stack after
Push constant:    [...]          ldc.i4 5       [..., 5]
Push constant:    [..., 5]       ldc.i4 3       [..., 5, 3]
Add:              [..., 5, 3]    add            [..., 8]
```

The rightmost value is always the **top** of the stack.

**Why is this useful?**

Expressions naturally form trees — `a + b * c` is `Add(a, Mul(b, c))`. A tree traversal always pushes operands before the operator that consumes them. The stack collects the partial results automatically:

```
ldc.i4 a      stack: [a]
ldc.i4 b      stack: [a, b]
ldc.i4 c      stack: [a, b, c]
mul           stack: [a, b*c]
add           stack: [a + b*c]
```

No explicit "store into register X, then load register X" required.

---

### 1.3 Method Frames

When the CLR calls a method it creates a **method frame** on the call stack. A method frame holds:

- **Arguments (arg slots):** the values passed by the caller. Slot 0 is always `this` for instance methods. Slot 1 is the first explicit parameter, slot 2 the second, and so on.
- **Local variables (local slots):** declared with `.locals init`. Indexed from 0. Separate from arg slots.
- **The evaluation stack:** starts empty. Must be empty again at `ret`.

```
┌─────────────────────────────────┐
│  arg 0  (this)                  │  ← ldarg.0
│  arg 1  (first param)           │  ← ldarg.1 / ldarg.s 1
│  arg 2  (second param)          │  ← ldarg.2 / ldarg.s 2
├─────────────────────────────────┤
│  local 0                        │  ← ldloc.0 / ldloc.s 0
│  local 1                        │  ← ldloc.1 / ldloc.s 1
├─────────────────────────────────┤
│  evaluation stack               │  ← grows/shrinks with push/pop ops
│    top → ...                    │
└─────────────────────────────────┘
```

**Reading and writing slots:**

```il
ldarg.0        ; push this onto evaluation stack
ldarg.s 1      ; push arg slot 1 onto evaluation stack
starg.s 1      ; pop top of evaluation stack → write into arg slot 1
ldloc.s 0      ; push local slot 0 onto evaluation stack
stloc.s 0      ; pop top of evaluation stack → write into local slot 0
```

**Field access** requires the object reference to already be on the evaluation stack:

```il
ldarg.0                    ; push this
ldfld int32 Foo::x         ; pop this, push this.x
ldarg.0                    ; push this
ldc.i4 42                  ; push 42
stfld int32 Foo::x         ; pop 42, pop this, write this.x = 42
```

---

### 1.4 The CLR Type System in CIL

CIL is **strongly typed**. Every value on the evaluation stack has a known type, and instructions enforce compatibility.

**Primitive types used by CPlus:**

| CIL name | Size | What it is |
|---|---|---|
| `int32` | 4 bytes | Signed 32-bit integer |
| `float32` | 4 bytes | IEEE 754 single-precision float |
| `bool` | 1 byte (stored as int32 internally) | Boolean — 0 = false, nonzero = true |
| `string` | reference | Managed reference to a `System.String` object |

**Class types** are always heap-allocated objects accessed through managed references. In CIL you write the class name directly as the type, e.g. `Counter`.

**Value types vs reference types:**

- `int32`, `float32`, `bool` are **value types** — stored directly on the evaluation stack or in a local slot.
- `string` and all class instances are **reference types** — the evaluation stack holds a pointer to a heap-allocated object, not the object itself.

This distinction matters for `ldfld`/`stfld`: the instruction receives an object reference (a pointer), not the object itself.

---

### 1.5 The Verifier and .maxstack

Before the JIT compiles a method, the CLR **verifier** checks that the CIL is type-safe and structurally valid. One of its checks is `.maxstack`:

```il
.method public instance void foo() cil managed
{
    .maxstack 3     // ← declaration: "this method never needs more than 3 slots"
    ...
}
```

The verifier tracks the stack depth through every code path. If it ever exceeds `.maxstack`, verification fails and the method cannot run.

**Rules the verifier enforces:**

- The stack must be empty at `ret` for void methods, and contain exactly one value for non-void methods.
- Every instruction must find the correct types on the stack (e.g. `add` requires two numeric values).
- Stack depth must be consistent at every point reachable by a branch — you cannot arrive at the same label with depth 2 from one path and depth 1 from another.
- `.maxstack` must be at least as large as the maximum depth reached on any path.

CPlus computes `.maxstack` by simulating the stack delta of every instruction in sequence and tracking the peak (see [Section 6.4](#64-maxstack-computation)).

---

### 1.6 The .il File Structure

A complete `.il` file must contain:

```il
// 1. Declare external assembly dependencies
.assembly extern mscorlib {}

// 2. Declare this assembly's identity
.assembly MyProgram {}

// 3. Define types
.class public MyClass extends [mscorlib]System.Object
{
    // Fields
    .field private int32 count

    // Methods
    .method public instance void increment(int32 amount) cil managed
    {
        .maxstack 3
        .locals init ([0] int32 temp)   // optional local variable declarations
        // ... CIL instructions ...
        ret
    }

    // Every class needs a constructor
    .method public specialname rtspecialname instance void .ctor() cil managed
    {
        .maxstack 1
        ldarg.0
        call instance void [mscorlib]System.Object::.ctor()
        ret
    }
}

// 4. Entry point (executables only) — must be a static method
.class public EntryPoint extends [mscorlib]System.Object
{
    .method public static void Main() cil managed
    {
        .entrypoint
        .maxstack 1
        // ... bootstrap code ...
        ret
    }
}
```

**Required elements:**
- `.assembly extern mscorlib {}` — without this, any reference to `System.Object`, `System.String`, etc. will fail to resolve.
- Every class must `extend` something — use `[mscorlib]System.Object` if there is no explicit base class.
- Every class must have a `.ctor` — the CLR refuses `newobj` on a class with no constructor.
- An executable must have exactly one method marked `.entrypoint` — it must be `static`.

---

### 1.7 The CIL Argument Convention

Every instance method has an **implicit first argument** — the `this` reference — in argument slot 0:

```
Slot 0  →  this
Slot 1  →  first declared parameter
Slot 2  →  second declared parameter
...
```

Static methods have no `this`, so slot 0 is the first declared parameter.

Instructions for accessing argument slots:

```il
ldarg.0         ; load slot 0 (this for instance methods)
ldarg.1         ; load slot 1
ldarg.2         ; load slot 2
ldarg.3         ; load slot 3
ldarg.s N       ; load slot N (N >= 4, or any slot — more general form)
starg.s N       ; store into slot N
```

**CPlus uses a different convention.** `LOAD_ARG 0` in CPlus means the first *declared* parameter (equivalent to CIL slot 1). `LOAD_THIS` is a separate opcode. When outputting CIL, the generator applies a +1 shift to all `LOAD_ARG`/`STORE_ARG` indices and maps `LOAD_THIS` directly to `ldarg.0`.

---

## 2. CPlus IL Data Model

The IL is a tree of plain data objects. None of them contain logic; all formatting and emission lives in `ILPrinter`.

```
ILProgram
└── List<ILClass> Classes

ILClass
├── string Name
├── List<ILField> Fields
└── List<ILMethod> Methods

ILField
├── string Name
├── DataType Type
├── bool IsPublic
└── bool IsImmutable       (stored; not yet emitted — CIL equivalent is `initonly`)

ILMethod
├── string Name
├── DataType ReturnType
├── bool IsPublic
├── List<ILLocal> Params     (index 0, 1, 2 ... in declaration order)
├── List<ILLocal> Locals     (index 0, 1, 2 ... separate from params)
└── List<ILInstruction> Instructions

ILLocal
├── string Name
├── DataType Type
└── int Index
```

### Instruction types

| Record type | Fields | Covers |
|---|---|---|
| `SimpleInstruction` | `Opcode` | All zero-operand ops: arithmetic, logic, stack, return, `LOAD_THIS` |
| `IndexInstruction` | `Opcode`, `Index` | `LOAD_LOCAL`, `STORE_LOCAL`, `LOAD_ARG`, `STORE_ARG` |
| `FieldInstruction` | `Opcode`, `OwnerClass`, `FieldName`, `FieldType` | `LOAD_FIELD`, `STORE_FIELD` |
| `NameInstruction` | `Opcode`, `Name` | `NEW` (class name), `LABEL`/`JUMP`/`JUMP_IF_*` (label name) |
| `LoadIntInstruction` | `Value: int` | `LOAD_CONST_INT` |
| `LoadFloatInstruction` | `Value: float` | `LOAD_CONST_FLOAT` |
| `LoadBoolInstruction` | `Value: bool` | `LOAD_CONST_BOOL` |
| `LoadStrInstruction` | `Value: string` | `LOAD_CONST_STR` |
| `InvokeInstruction` | `Opcode`, `ClassName`, `MethodName`, `ArgCount`, `ReturnType`, `ParamTypes` | `INVOKE`, `INVOKE_VOID` |

**Why `FieldInstruction` instead of `NameInstruction`?**
CIL `ldfld`/`stfld` need the owner class and field type in the output. A plain `NameInstruction` only carries one string (the field name). `FieldInstruction` carries all three pieces needed to emit the complete CIL signature.

**Why does `InvokeInstruction` carry `ReturnType` and `ParamTypes`?**
CIL `call` needs the full method signature: `call instance <ret> <Class>::<method>(<p1>, <p2>...)`. The semantic checker has already resolved these types; they are threaded through into the instruction so `Dump()` can emit the correct CIL without re-querying the symbol table.

---

## 3. The CPlus Value Stack

Every method has its own value stack, empty at entry.

**Notation:** `[..., a, b]` — rightmost is top. `→` means after the instruction.

**Key rules:**
- Instructions that consume values **pop** them.
- Instructions that produce values **push** them.
- At `ret` (void method): stack must be empty.
- At `ret` (non-void method): stack must contain exactly one value.
- Between statements the stack returns to empty.

**Stack deltas for each instruction category:**

| Operation | Delta |
|---|---|
| Push constant / `LOAD_THIS` / `LOAD_ARG` / `LOAD_LOCAL` | +1 |
| `STORE_ARG` / `STORE_LOCAL` / `POP` / `RETURN_VAL` | -1 |
| `LOAD_FIELD` | 0 (pop obj, push value — net 0) |
| `STORE_FIELD` | -2 (pop obj + value) |
| `NEW` | +1 |
| `DUP` | +1 |
| Binary arithmetic / comparison / logical / `CONCAT` | -1 (pop 2, push 1) |
| Unary `NEG` / `NOT` / `INT_TO_FLOAT` | 0 (pop 1, push 1) |
| `INVOKE` | `-(1 + ArgCount) + 1` = `-ArgCount` |
| `INVOKE_VOID` | `-(1 + ArgCount)` |

---

## 4. CPlus Instruction Set Reference

### Constant loads

```
LOAD_CONST_INT    <int>       [...] → [..., int]
LOAD_CONST_FLOAT  <float>     [...] → [..., float]
LOAD_CONST_BOOL   <bool>      [...] → [..., bool]
LOAD_CONST_STR    <string>    [...] → [..., string]
```

### Variable access

```
LOAD_ARG   <index>    [...] → [..., value]   push parameter[index]
STORE_ARG  <index>    [..., value] → [...]   pop → parameter[index]
LOAD_LOCAL <index>    [...] → [..., value]   push local[index]
STORE_LOCAL <index>   [..., value] → [...]   pop → local[index]
```

Parameters and locals have **separate** index spaces, both starting at 0.

### Field access

```
LOAD_FIELD  <ownerClass> <fieldName> <fieldType>
    [..., obj] → [..., value]   pop obj, push obj.field

STORE_FIELD <ownerClass> <fieldName> <fieldType>
    [..., obj, value] → [...]   pop value then obj, write obj.field = value
    Stack order: obj must be BELOW value.
```

### Object and this

```
LOAD_THIS     [...] → [..., this]
NEW <class>   [...] → [..., obj]   allocate new heap object, all fields defaulted
```

### Method invocation

Stack before either invoke: `[..., receiver, arg0, arg1, ..., argN-1]`

```
INVOKE      <class> <method> <argCount> <returnType> <paramTypes>
    [..., receiver, args...] → [..., returnValue]

INVOKE_VOID <class> <method> <argCount> <returnType> <paramTypes>
    [..., receiver, args...] → [...]
```

`ArgCount` does not include the receiver.

### Arithmetic, logic, comparison

All binary ops: pop right, pop left, push result.

```
ADD / SUB / MUL / DIV    int or float
NEG                      int or float   (unary)
AND / OR                 bool
NOT                      bool           (unary)
EQ / NEQ                 any compatible types → bool
LT / LTE / GT / GTE      int or float → bool
CONCAT                   string + string → string
INT_TO_FLOAT             int → float   (widening)
```

### Control flow

```
LABEL <name>            pseudo — marks jump target, no runtime effect
JUMP <label>            unconditional branch
JUMP_IF_TRUE <label>    pop bool, branch if true
JUMP_IF_FALSE <label>   pop bool, branch if false
```

### Stack and return

```
POP        [..., value] → [...]
DUP        [..., value] → [..., value, value]
RETURN     void return
RETURN_VAL pop value and return it
```

---

## 5. AST → IL Generation Strategies

The generator is `ILGenerator : IASTVisitor<object?>`. It is a **side-effect visitor** — all methods return `null`; they build state by calling `Emit(instr)` which appends to `_currentMethod.Instructions`.

### 5.1 Two-pass compiler context

The generator runs **after** `SematicCheckerAuto`. By then every `Expression` node has its `ResolvedType` populated. The generator relies on this to resolve class names for field and invoke instructions without re-querying the symbol table for every expression.

The `CompileEnviroment` is passed through because the generator needs `env.SymbolTable` to look up full method signatures (`ReturnType`, `ParamTypes`) and field types — information that `ResolvedType` alone doesn't carry.

### 5.2 Per-method index tables

When entering a `MethodDecl`, the generator builds two dictionaries:

```csharp
_paramIndex[name] = i   // index 0, 1, 2... in declaration order
_localIndex[name] = i   // index 0, 1, 2... in declaration order
```

When resolving an `ID` or `Assign` LHS, the lookup priority is:
1. `_paramIndex` → emit `LOAD_ARG` / `STORE_ARG`
2. `_localIndex` → emit `LOAD_LOCAL` / `STORE_LOCAL`
3. Neither → must be a field of `this` → emit `LOAD_THIS` + `LOAD_FIELD` / `STORE_FIELD`

### 5.3 Generating field access

**Read:**
```
visit(FieldAccess { Obj, FieldName })
  → emit Obj
  → emit FieldInstruction(LOAD_FIELD, ownerClass, fieldName, fieldType)

visit(ID { name }) where name is a field
  → emit SimpleInstruction(LOAD_THIS)
  → emit FieldInstruction(LOAD_FIELD, _currentClassName, name, fieldType)
```

**Write:**
```
visit(Assign { LHS = FieldAccess { Obj, FieldName }, Expression })
  → emit Obj                          ← push receiver
  → emit Expression                   ← push new value
  → emit FieldInstruction(STORE_FIELD, ownerClass, fieldName, fieldType)

visit(Assign { LHS = ID { name }, Expression }) where name is a field
  → emit SimpleInstruction(LOAD_THIS) ← push receiver
  → emit Expression                   ← push new value
  → emit FieldInstruction(STORE_FIELD, _currentClassName, name, fieldType)
```

The receiver must be below the value on the stack for `STORE_FIELD`. This is why `LOAD_THIS` (or the receiver expression) is emitted **before** the RHS expression.

### 5.4 Generating method calls

```
visit(CallMethodStmt { Obj, Method, Params })
  → emit Obj
  → foreach arg: emit arg
  → look up method symbol in env.SymbolTable
  → emit InvokeInstruction(INVOKE_VOID, className, method.Name,
                            argc, returnType, paramTypes)

visit(CallExpression { Obj, Method, Params })
  → same, but INVOKE instead of INVOKE_VOID
```

The class name comes from `ReceiverClassName(expr)`:
- If `expr.ResolvedType` is `ClassType` → use its class name.
- If `expr` is `ThisLiteral` → use `_currentClassName` (fallback, because `ThisLiteral.ResolvedType` may be null after the semantic pass).

### 5.5 Generating binary operators

```
visit(BinaryOp { Left, Right, Op })
  → emit Left
  → if Left is int and Right is float: emit INT_TO_FLOAT
  → emit Right
  → if Left is float and Right is int: emit INT_TO_FLOAT
  → emit opcode for Op
```

String `+` maps to `CONCAT`; numeric `+` maps to `ADD`. The `+` case is disambiguated by checking `ResolvedType` on either operand.

### 5.6 Implicit void return

After emitting all statements in a void method, if the last instruction is not already `RETURN`, the generator appends one automatically. This handles methods with no explicit `return` statement.

### 5.7 Local initializers

Local declarations with a value expression are emitted at the top of the method body, before the statements:

```
foreach local declaration d where d.Value != null:
  → emit d.Value
  → emit STORE_LOCAL _localIndex[d.Name]
```

---

## 6. ILProgram → CIL Mapping (Complete)

All of this logic lives in `ILPrinter` inside `ILProgram.cs`. `Dump()` on `ILProgram`, `ILClass`, and `ILMethod` calls into `ILPrinter` to convert the in-memory object graph into CIL text strings.

### 6.1 Type Name Mapping

Every `DataType` in the CPlus type hierarchy maps to a CIL type token:

| CPlus `DataType` | CIL token | Notes |
|---|---|---|
| `IntType` | `int32` | Signed 32-bit integer |
| `FloatType` | `float32` | Single-precision IEEE 754 |
| `BooleanType` | `bool` | Stored as `int32` internally by CLR |
| `StringType` | `string` | `[mscorlib]System.String` reference |
| `VoidType` | `void` | Used only in method return positions |
| `ClassType(name)` | `name` | The class name verbatim |

This mapping is applied everywhere a type appears in the output: field declarations, method signatures, `ldfld`/`stfld` operands, and `call` parameter lists.

---

### 6.2 Instruction-by-Instruction CIL Output

Every CPlus `ILInstruction` object is converted to one or more CIL text lines by `ILPrinter.InstrLines`. The complete mapping:

#### Constant loads

| CPlus instruction | CIL output |
|---|---|
| `LoadIntInstruction(v)` | `ldc.i4 v` |
| `LoadFloatInstruction(v)` | `ldc.r4 v` (formatted with `R` round-trip format) |
| `LoadBoolInstruction(true)` | `ldc.i4.1` |
| `LoadBoolInstruction(false)` | `ldc.i4.0` |
| `LoadStrInstruction(s)` | `ldstr "s"` |

#### Variable access — index shift applied here

| CPlus instruction | CIL output | Why |
|---|---|---|
| `IndexInstruction(LOAD_ARG, n)` | `ldarg.s n+1` | CIL slot 0 = `this`; shift all params +1 |
| `IndexInstruction(STORE_ARG, n)` | `starg.s n+1` | Same shift |
| `IndexInstruction(LOAD_LOCAL, n)` | `ldloc.s n` | Locals are unshifted |
| `IndexInstruction(STORE_LOCAL, n)` | `stloc.s n` | Locals are unshifted |

#### Field access — fully qualified signatures

| CPlus instruction | CIL output |
|---|---|
| `FieldInstruction(LOAD_FIELD, owner, field, type)` | `ldfld {TypeStr(type)} {owner}::{field}` |
| `FieldInstruction(STORE_FIELD, owner, field, type)` | `stfld {TypeStr(type)} {owner}::{field}` |

Example: `FieldInstruction(LOAD_FIELD, "Counter", "count", IntType)` → `ldfld int32 Counter::count`

#### Object creation

| CPlus instruction | CIL output |
|---|---|
| `NameInstruction(NEW, className)` | `newobj instance void {className}::.ctor()` |

#### Method invocation — both opcodes map to `call`

| CPlus instruction | CIL output |
|---|---|
| `InvokeInstruction(INVOKE, cls, mth, argc, ret, params)` | `call instance {ret} {cls}::{mth}({params})` |
| `InvokeInstruction(INVOKE_VOID, cls, mth, argc, ret, params)` | `call instance void {cls}::{mth}({params})` |

Both `INVOKE` and `INVOKE_VOID` map to `call` (not `callvirt`) because CPlus has no inheritance or virtual dispatch. The distinction between them exists only in how the CPlus evaluation stack is managed — `INVOKE` leaves a return value on the stack; `INVOKE_VOID` does not.

Example: `InvokeInstruction(INVOKE_VOID, "Counter", "increment", 1, VoidType, [IntType])`
→ `call instance void Counter::increment(int32)`

#### Control flow

| CPlus instruction | CIL output |
|---|---|
| `NameInstruction(LABEL, name)` | `name:` (the label itself, less indented) |
| `NameInstruction(JUMP, name)` | `br name` |
| `NameInstruction(JUMP_IF_TRUE, name)` | `brtrue name` |
| `NameInstruction(JUMP_IF_FALSE, name)` | `brfalse name` |

#### Simple (zero-operand) instructions

| CPlus opcode | CIL output | Notes |
|---|---|---|
| `LOAD_THIS` | `ldarg.0` | `this` is always CIL arg slot 0 |
| `LOAD_NULL` | `ldnull` | |
| `POP` | `pop` | |
| `DUP` | `dup` | |
| `RETURN` | `ret` | Both RETURN and RETURN_VAL → `ret` |
| `RETURN_VAL` | `ret` | The value on the stack is the return value |
| `ADD` | `add` | |
| `SUB` | `sub` | |
| `MUL` | `mul` | |
| `DIV` | `div` | |
| `NEG` | `neg` | |
| `NOT` | `ldc.i4.0` + `ceq` | No single CIL NOT — see 6.3 |
| `CONCAT` | `call string [mscorlib]System.String::Concat(string, string)` | String + calls BCL |
| `INT_TO_FLOAT` | `conv.r4` | Widen int32 to float32 |
| `EQ` | `ceq` | |
| `NEQ` | `ceq` + `ldc.i4.0` + `ceq` | See 6.3 |
| `LT` | `clt` | |
| `LTE` | `cgt` + `ldc.i4.0` + `ceq` | See 6.3 |
| `GT` | `cgt` | |
| `GTE` | `clt` + `ldc.i4.0` + `ceq` | See 6.3 |
| `AND` | `and` | Bitwise AND on int32 — works for bool (0/1) |
| `OR` | `or` | Bitwise OR on int32 — works for bool (0/1) |

---

### 6.3 Multi-Instruction Expansions Explained

CIL has no direct opcodes for `!=`, `<=`, `>=`, or boolean `NOT`. These must be expressed using a combination of available opcodes.

#### `NOT` — logical negation of a bool

CIL has no `not` for booleans (it has `not` only for bitwise integer negation). The pattern is to compare the value against `0` using `ceq`, which returns 1 if the value was 0 (i.e. false) and 0 if the value was nonzero (i.e. true):

```
Stack before:  [..., b]
ldc.i4.0       [..., b, 0]
ceq            [..., b == 0]   ← 1 if b was false, 0 if b was true
Stack after:   [..., !b]
```

#### `NEQ` — not-equal comparison

CIL `ceq` gives you equality. Negate it with the `NOT` pattern:

```
Stack before:  [..., a, b]
ceq            [..., a == b]   ← 1 if equal, 0 if not
ldc.i4.0       [..., a==b, 0]
ceq            [..., (a==b) == 0]  ← 1 if not equal, 0 if equal
Stack after:   [..., a != b]
```

#### `LTE` — less-than-or-equal

`a <= b` is the same as `NOT (a > b)`:

```
Stack before:  [..., a, b]
cgt            [..., a > b]    ← 1 if a greater than b
ldc.i4.0       [..., a>b, 0]
ceq            [..., (a>b) == 0]  ← 1 if a was NOT greater (i.e. a <= b)
Stack after:   [..., a <= b]
```

#### `GTE` — greater-than-or-equal

`a >= b` is the same as `NOT (a < b)`:

```
Stack before:  [..., a, b]
clt            [..., a < b]    ← 1 if a less than b
ldc.i4.0       [..., a<b, 0]
ceq            [..., (a<b) == 0]  ← 1 if a was NOT less (i.e. a >= b)
Stack after:   [..., a >= b]
```

---

### 6.4 MaxStack Computation Algorithm

`ILPrinter.ComputeMaxStack` simulates the evaluation stack depth over the entire instruction list and returns the peak value. The CLR requires this value to be declared as `.maxstack` in the method header — and because the header comes *before* the instructions in the output, the simulation must run over the complete instruction list first, before `Dump()` can write anything.

---

#### 6.4.1 The Core Loop

The fundamental idea is simple: walk every instruction, maintain a running `depth`, and record the highest `depth` ever reached.

```csharp
int depth = 0, max = 0;
foreach (var instr in instructions)
{
    depth += StackDelta(instr);
    if (depth > max) max = depth;
}
return Math.Max(1, max);
```

`StackDelta` returns the **net** change to the stack for each instruction — positive means it pushes more than it pops, negative means it pops more than it pushes.

For a simple sequence like `var a = 10; var b = 100; b = a + b`:

```
Instruction    Delta    depth    max
-----------    -----    -----    ---
ldc.i4 10      +1       1        1
stloc.s 0      -1       0        1
ldc.i4 100     +1       1        1
stloc.s 1      -1       0        1
ldloc.s 0      +1       1        1
ldloc.s 1      +1       2        2    ← new max
add            -1       1        2
stloc.s 1      -1       0        2
```

Result: `.maxstack 2`.

---

#### 6.4.2 The Problem — Multi-Instruction Expansions

The core loop works perfectly when each CPlus opcode maps to exactly one CIL instruction. But some CPlus opcodes expand to **multiple CIL instructions** (see Section 6.3). `StackDelta` only knows the *net* effect of the whole expansion — it does not see the intermediate steps.

The CLR verifier, however, sees every individual CIL instruction. If the depth temporarily spikes during an expansion and then comes back down, the net delta is zero — but the CLR still enforces `.maxstack` at every intermediate step.

**Example: `NOT` expands to `ldc.i4.0` + `ceq`**

Suppose `depth = 2` when `NOT` is encountered.

What `StackDelta(NOT)` returns: `0` (net: pop 1 bool, push 1 bool).

What the CLR actually sees:
```
depth 2  →  ldc.i4.0  →  depth 3  →  ceq  →  depth 2
```

The depth hit **3** in the middle. If we only track net deltas, we declare `.maxstack 2` — and the CLR verifier rejects the method because depth 3 exceeds it.

---

#### 6.4.3 The Fix — Intermediate Peak Check

Before applying the net delta of a multi-expansion instruction, the algorithm checks what the depth will be at the *highest intermediate point* inside that expansion, and updates `max` if needed:

```csharp
foreach (var instr in instructions)
{
    // Step 1: check intermediate peak for multi-expansion opcodes
    if (instr is SimpleInstruction si)
    {
        int peak = si.Op switch
        {
            Opcode.NOT => depth + 1,
            Opcode.NEQ or Opcode.LTE or Opcode.GTE => depth - 1 + 1,
            _ => depth
        };
        if (peak > max) max = peak;
    }

    // Step 2: apply net delta
    depth += StackDelta(instr);
    if (depth > max) max = depth;
}
```

**Why those specific peak formulas:**

`NOT` expands to `ldc.i4.0` (+1) then `ceq` (-1). The peak occurs after the `ldc`, before the `ceq`:
```
peak = depth + 1
```

`NEQ`, `LTE`, `GTE` all expand to three instructions: a comparison op (`ceq`/`cgt`/`clt`) which pops 2 and pushes 1 (depth-1), then `ldc.i4.0` which pushes 1 (depth), then `ceq` which pops 2 and pushes 1 (depth-1). The peak occurs after the `ldc.i4.0`, in the middle:
```
peak = (depth - 1) + 1 = depth
```

This is the same as the current depth, so it only makes a difference if `depth` is itself a new max at that point — which is still worth checking.

---

#### 6.4.4 Stack Deltas Reference

The complete delta table used by `StackDelta`:

| Instruction | Delta | Reason |
|---|---|---|
| `LoadIntInstruction` | +1 | pushes one int |
| `LoadFloatInstruction` | +1 | pushes one float |
| `LoadBoolInstruction` | +1 | pushes one bool |
| `LoadStrInstruction` | +1 | pushes one string reference |
| `IndexInstruction(LOAD_ARG / LOAD_LOCAL)` | +1 | pushes the slot value |
| `IndexInstruction(STORE_ARG / STORE_LOCAL)` | -1 | pops into the slot |
| `FieldInstruction(LOAD_FIELD)` | 0 | pops obj, pushes value — net zero |
| `FieldInstruction(STORE_FIELD)` | -2 | pops obj and value |
| `NameInstruction(NEW)` | +1 | pushes new object reference |
| `NameInstruction(LABEL / JUMP)` | 0 | no stack effect |
| `NameInstruction(JUMP_IF_TRUE / JUMP_IF_FALSE)` | -1 | pops the condition bool |
| `InvokeInstruction(INVOKE)` | `-(1 + ArgCount) + 1` = `-ArgCount` | pops receiver+args, pushes return value |
| `InvokeInstruction(INVOKE_VOID)` | `-(1 + ArgCount)` | pops receiver+args, pushes nothing |
| `LOAD_THIS` | +1 | pushes `this` reference |
| `LOAD_NULL` | +1 | pushes null reference |
| `POP` | -1 | discards top |
| `DUP` | +1 | copies top |
| `RETURN` | 0 | stack must already be empty |
| `RETURN_VAL` | -1 | pops the return value |
| `ADD / SUB / MUL / DIV` | -1 | pops 2, pushes 1 |
| `NEG` | 0 | pops 1, pushes 1 |
| `NOT` | 0 | net: pops 1, pushes 1 (but see 6.4.2) |
| `CONCAT` | -1 | pops 2 strings, pushes 1 |
| `INT_TO_FLOAT` | 0 | pops int, pushes float |
| `EQ / NEQ / LT / LTE / GT / GTE` | -1 | pops 2, pushes 1 bool (but see 6.4.2 for NEQ/LTE/GTE) |
| `AND / OR` | -1 | pops 2 bools, pushes 1 |

---

#### 6.4.5 Worked Examples

**Example 1 — simple arithmetic: `b = a + b`**

Locals: `a` at slot 0, `b` at slot 1.

```
Instruction    Delta    depth    peak check    max
-----------    -----    -----    ----------    ---
ldloc.s 0      +1       1        —             1
ldloc.s 1      +1       2        —             2
add            -1       1        —             2
stloc.s 1      -1       0        —             2
```

`.maxstack 2`

---

**Example 2 — boolean NOT: `!flag`**

Local `flag` at slot 0, starting at depth 0.

```
Instruction    Delta    depth    peak check       max
-----------    -----    -----    ----------       ---
ldloc.s 0      +1       1        —                1
NOT            0        1        1+1 = 2 ← !      2
```

Without the peak check, `max` would be 1. With it, `max` = 2.

`.maxstack 2`

---

**Example 3 — inequality: `a != b`**

```
Instruction    Delta    depth    peak check          max
-----------    -----    -----    ----------          ---
ldloc.s 0      +1       1        —                   1
ldloc.s 1      +1       2        —                   2
NEQ            -1       1        (2-1)+1 = 2 ← !     2
```

At the `NEQ` peak check: `depth` is 2, so peak = `depth - 1 + 1` = 2. Already the max, so no change here — but it correctly confirms max stays 2 through the expansion.

`.maxstack 2`

---

**Example 4 — NOT at elevated depth: `!(a == b)`**

```
Instruction    Delta    depth    peak check    max
-----------    -----    -----    ----------    ---
ldloc.s 0      +1       1        —             1
ldloc.s 1      +1       2        —             2
EQ             -1       1        —             2
NOT            0        1        1+1 = 2       2
```

`.maxstack 2` — the peak check for `NOT` catches that depth 2 would be reached inside the expansion even though depth was only 1 when `NOT` started.

---

**The minimum of 1**

```csharp
return Math.Max(1, max);
```

A method that never pushes anything (e.g. an empty void method that just runs `ret`) leaves `max = 0`. The CLR still requires `.maxstack 1` as a minimum, so the result is clamped.

---

### 6.5 ILMethod.Dump()

```
.method {visibility} instance {retType} {name}({param0Type} {param0Name}, ...) cil managed
{
    .maxstack {ComputeMaxStack(Instructions)}
    .locals init ([0] {type0} {name0}, [1] {type1} {name1}, ...)  // only if locals exist
    {InstrLines for each instruction, indented 8 spaces}
    // Labels get only 4 spaces so they visually stand out
}
```

Each instruction is passed through `ILPrinter.InstrLines(instr)` which returns one or more strings. A line that ends in `:` is treated as a label and gets 4-space indentation instead of 8.

---

### 6.6 ILClass.Dump()

```
.class public {Name} extends [mscorlib]System.Object
{
    .field {public|private} {TypeStr(Type)} {Name}
    // ... all fields ...

    // ... all methods from ILMethod.Dump() ...

    // Auto-generated constructor (always appended):
    .method public specialname rtspecialname instance void .ctor() cil managed
    {
        .maxstack 1
        ldarg.0
        call instance void [mscorlib]System.Object::.ctor()
        ret
    }
}
```

Every CPlus class is emitted as `public`. The `.ctor` is always appended regardless of whether the class has any `new` calls — the CLR requires it to exist.

`specialname rtspecialname` are CLR flags marking this as a special constructor method rather than a regular user-defined method.

---

### 6.7 ILProgram.Dump()

```
.assembly extern mscorlib {}
.assembly CPlus {}

{ILClass.Dump() for each class}

// Only emitted if a class with a public void main() exists:
.class public EntryPoint extends [mscorlib]System.Object
{
    .method public static void Main() cil managed
    {
        .entrypoint
        .maxstack 2
        newobj instance void {ownerClass}::.ctor()
        call instance void {ownerClass}::main()
        ret
    }
}
```

**Why the synthetic `EntryPoint`?**

The CLR requires `.entrypoint` to be on a `static` method. CPlus `main()` is an instance method. The synthetic `EntryPoint.Main` bridges this by allocating an instance of the entry class, then calling `main()` on it. The `ownerClass` is the first class in the program that has a `public void main()` method with no parameters.

---

## 7. Full Worked Example

**Source:**

```
class Counter {
    private int count;

    public void increment(int amount) {
        count = count + amount;
    }

    public int get() {
        return count;
    }

    public void main() {
        this.increment(1);
    }
}
```

---

**`increment` — AST walk trace**

Statement: `count = count + amount` where `count` is a field, `amount` is a parameter.

The LHS is `ID("count")` — not in `_paramIndex`, not in `_localIndex`, so it is a field of `this`. The generator emits `LOAD_THIS` first (receiver for the upcoming `STORE_FIELD`), then walks the RHS.

The RHS is `BinaryOp("+", ID("count"), ID("amount"))`. Left operand: `ID("count")` is a field → `LOAD_THIS` + `LOAD_FIELD`. Right operand: `ID("amount")` is parameter 0 → `LOAD_ARG 0`. Then `ADD`.

CPlus IL sequence:

```
LOAD_THIS                             receiver for STORE_FIELD
LOAD_THIS                             receiver for LOAD_FIELD
LOAD_FIELD  Counter "count" IntType   this.count
LOAD_ARG    0                         amount (CPlus index 0)
ADD
STORE_FIELD Counter "count" IntType
RETURN                                (implicit void return)
```

Mapping to CIL (per Section 6.2):

| CPlus IL | CIL | Rule |
|---|---|---|
| `LOAD_THIS` | `ldarg.0` | `LOAD_THIS` → `ldarg.0` |
| `LOAD_THIS` | `ldarg.0` | same |
| `LOAD_FIELD Counter count IntType` | `ldfld int32 Counter::count` | `int` → `int32`, fully qualified |
| `LOAD_ARG 0` | `ldarg.s 1` | index shift +1 |
| `ADD` | `add` | direct |
| `STORE_FIELD Counter count IntType` | `stfld int32 Counter::count` | fully qualified |
| `RETURN` | `ret` | direct |

MaxStack simulation: peaks at 3 (after the two `ldarg.0` and the `ldfld` — three values on the stack before `ldarg.s 1` is pushed for the ADD).

ilasm output:

```il
.method public instance void increment(int32 amount) cil managed
{
    .maxstack 3
    ldarg.0
    ldarg.0
    ldfld int32 Counter::count
    ldarg.s 1
    add
    stfld int32 Counter::count
    ret
}
```

---

**`get` — AST walk trace**

Statement: `return count`.

`count` is a field → `LOAD_THIS` + `LOAD_FIELD`. Then `RETURN_VAL`.

| CPlus IL | CIL | Rule |
|---|---|---|
| `LOAD_THIS` | `ldarg.0` | direct |
| `LOAD_FIELD Counter count IntType` | `ldfld int32 Counter::count` | fully qualified |
| `RETURN_VAL` | `ret` | direct |

MaxStack: peaks at 1.

```il
.method public instance int32 get() cil managed
{
    .maxstack 1
    ldarg.0
    ldfld int32 Counter::count
    ret
}
```

---

**`main` — AST walk trace**

Statement: `this.increment(1)` — a `CallMethodStmt`.

Receiver `ThisLiteral` → `LOAD_THIS`. Argument `IntLiteral(1)` → `LOAD_CONST_INT 1`. Then `INVOKE_VOID` with signature looked up from the symbol table: `Counter::increment(int32) → void`.

| CPlus IL | CIL | Rule |
|---|---|---|
| `LOAD_THIS` | `ldarg.0` | direct |
| `LOAD_CONST_INT 1` | `ldc.i4 1` | direct |
| `INVOKE_VOID Counter increment 1 VoidType [IntType]` | `call instance void Counter::increment(int32)` | fully qualified, `void` return |
| `RETURN` | `ret` | direct |

MaxStack: peaks at 2 (receiver + argument before the call pops both).

```il
.method public instance void main() cil managed
{
    .maxstack 2
    ldarg.0
    ldc.i4 1
    call instance void Counter::increment(int32)
    ret
}
```

---

**Complete ilasm output for `counter.cplus`:**

```il
.assembly extern mscorlib {}
.assembly CPlus {}

.class public Counter extends [mscorlib]System.Object
{
    .field private int32 count

    .method public instance void increment(int32 amount) cil managed
    {
        .maxstack 3
        ldarg.0
        ldarg.0
        ldfld int32 Counter::count
        ldarg.s 1
        add
        stfld int32 Counter::count
        ret
    }

    .method public instance int32 get() cil managed
    {
        .maxstack 1
        ldarg.0
        ldfld int32 Counter::count
        ret
    }

    .method public instance void main() cil managed
    {
        .maxstack 2
        ldarg.0
        ldc.i4 1
        call instance void Counter::increment(int32)
        ret
    }

    .method public specialname rtspecialname instance void .ctor() cil managed
    {
        .maxstack 1
        ldarg.0
        call instance void [mscorlib]System.Object::.ctor()
        ret
    }
}

.class public EntryPoint extends [mscorlib]System.Object
{
    .method public static void Main() cil managed
    {
        .entrypoint
        .maxstack 2
        newobj instance void Counter::.ctor()
        call instance void Counter::main()
        ret
    }
}
```
