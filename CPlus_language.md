# CPlus Language Reference

CPlus is a statically-typed, class-based object-oriented language. Every piece
of code lives inside a class. There are no top-level functions, global
variables, or entry-point procedures — CPlus is purely OOP at the top level.

---

## Table of Contents

1. [Program Structure](#1-program-structure)
2. [Lexical Rules](#2-lexical-rules)
   - 2.1 [Comments](#21-comments)
   - 2.2 [Identifiers](#22-identifiers)
   - 2.3 [Keywords](#23-keywords)
   - 2.4 [Operators & Separators](#24-operators--separators)
3. [Types](#3-types)
   - 3.1 [Primitive Types](#31-primitive-types)
   - 3.2 [Class Types](#32-class-types)
   - 3.3 [void](#33-void)
   - 3.4 [Type Compatibility](#34-type-compatibility)
4. [Literals](#4-literals)
5. [Classes](#5-classes)
6. [Fields](#6-fields)
   - 6.1 [Mutable Fields](#61-mutable-fields)
   - 6.2 [Immutable Fields](#62-immutable-fields-immut)
7. [Methods](#7-methods)
   - 7.1 [Signature](#71-signature)
   - 7.2 [Parameters](#72-parameters)
   - 7.3 [Method Body](#73-method-body)
   - 7.4 [Return Rules](#74-return-rules)
8. [Statements](#8-statements)
   - 8.1 [Assignment](#81-assignment)
   - 8.2 [Return](#82-return)
   - 8.3 [Method Call Statement](#83-method-call-statement)
9. [Expressions](#9-expressions)
   - 9.1 [Operator Precedence](#91-operator-precedence)
   - 9.2 [Arithmetic](#92-arithmetic)
   - 9.3 [Equality](#93-equality)
   - 9.4 [Logical NOT](#94-logical-not)
   - 9.5 [Unary Plus and Minus](#95-unary-plus-and-minus)
   - 9.6 [Member Access and Method Calls](#96-member-access-and-method-calls)
   - 9.7 [Object Creation](#97-object-creation)
   - 9.8 [this](#98-this)
10. [Visibility](#10-visibility)
11. [Immutability](#11-immutability)
12. [Scoping Rules](#12-scoping-rules)
13. [Error Reference](#13-error-reference)
14. [What Is Not Supported Yet](#14-what-is-not-supported-yet)
15. [Complete Example](#15-complete-example)

---

## 1. Program Structure

A CPlus source file is a sequence of zero or more class declarations, followed
by end-of-file.

```
program := class_decl*
```

There is no package or module system. All classes in a file share the same
namespace. Order of declaration does not matter — a class can reference another
class declared later in the same file.

---

## 2. Lexical Rules

### 2.1 Comments

**Line comment** — starts with `#`, runs to the end of the line:

```
# This is a line comment
```

**Block comment** — delimited by `/*` and `*/`, can span multiple lines:

```
/*
   This is a block comment.
*/
```

Both comment styles are discarded by the lexer and produce no tokens.

---

### 2.2 Identifiers

An identifier starts with an underscore or a letter, followed by zero or more
underscores, letters, or digits.

```
[_a-zA-Z][_a-zA-Z0-9]*
```

Examples of valid identifiers: `x`, `_count`, `MyClass`, `value1`, `__init`

Identifiers are case-sensitive. `Counter` and `counter` are different names.

---

### 2.3 Keywords

The following words are reserved and cannot be used as identifiers:

| Keyword    | Purpose                          |
|------------|----------------------------------|
| `class`    | Declares a class                 |
| `public`   | Public visibility modifier       |
| `private`  | Private visibility modifier      |
| `immut`    | Marks a field or variable as immutable (constant) |
| `int`      | Integer primitive type           |
| `float`    | Floating-point primitive type    |
| `boolean`  | Boolean primitive type           |
| `string`   | String primitive type            |
| `void`     | No-return type for methods       |
| `new`      | Allocates a new object           |
| `return`   | Returns from a method            |
| `this`     | Reference to the current object  |
| `true`     | Boolean literal true             |
| `false`    | Boolean literal false            |

---

### 2.4 Operators & Separators

**Arithmetic operators:**

| Symbol | Meaning        |
|--------|----------------|
| `+`    | Addition       |
| `-`    | Subtraction    |
| `*`    | Multiplication |
| `/`    | Division       |

**Comparison operators:**

| Symbol | Meaning      |
|--------|--------------|
| `==`   | Equal to     |
| `!=`   | Not equal to |

**Logical operators:**

| Symbol | Meaning     |
|--------|-------------|
| `!`    | Logical NOT |

**Assignment:**

| Symbol | Meaning |
|--------|---------|
| `=`    | Assign  |

**Separators:**

| Symbol | Name            | Used for                          |
|--------|-----------------|-----------------------------------|
| `{`    | Left brace      | Opens a class or method body      |
| `}`    | Right brace     | Closes a class or method body     |
| `(`    | Left paren      | Opens a method parameter list / grouping |
| `)`    | Right paren     | Closes a method parameter list / grouping |
| `[`    | Left bracket    | Reserved (arrays, not yet active) |
| `]`    | Right bracket   | Reserved (arrays, not yet active) |
| `;`    | Semicolon       | Terminates declarations and statements |
| `.`    | Dot             | Member access                     |
| `,`    | Comma           | Separates parameters / arguments  |
| `:`    | Colon           | Reserved                          |

---

## 3. Types

### 3.1 Primitive Types

| Type      | Description                                          | Default value |
|-----------|------------------------------------------------------|---------------|
| `int`     | 32-bit signed integer                                | `0`           |
| `float`   | 32-bit floating-point number                         | `0.0`         |
| `boolean` | Boolean (`true` or `false`)                          | `false`       |
| `string`  | Immutable sequence of characters                     | `""`          |

---

### 3.2 Class Types

Any class name is a valid type. A variable of a class type holds an object
reference (or `null` implicitly when a field is not initialised).

```
Counter c;          # c is a reference to a Counter object
c = new Counter();
```

---

### 3.3 void

`void` is only valid as a method return type. It cannot be used as a variable
type or a parameter type.

---

### 3.4 Type Compatibility

CPlus uses **nominal** typing with one widening rule:

| Assignment target | Acceptable source types |
|-------------------|-------------------------|
| `int`             | `int`                   |
| `float`           | `float`, `int`  ← int widens to float |
| `boolean`         | `boolean`               |
| `string`          | `string`                |
| `ClassName`       | `ClassName` (same class only — no inheritance) |

There is no implicit narrowing. Assigning a `float` expression to an `int`
variable is a type error.

---

## 4. Literals

### Integer literals

A sequence of one or more decimal digits.

```
0    42    1000
```

### Float literals

Digits followed by a dot, optionally followed by more digits.
The trailing-dot form is valid — `3.` is the float `3.0`.

```
3.14    2.    0.5    100.0
```

A bare integer followed by nothing is an `int`, not a `float`.

### Boolean literals

```
true    false
```

### String literals

A sequence of characters enclosed in double quotes.

```
"hello"
"line one\nline two"
"tab\there"
```

Supported escape sequences inside strings:

| Sequence | Meaning            |
|----------|--------------------|
| `\b`     | Backspace          |
| `\t`     | Horizontal tab     |
| `\n`     | Newline (LF)       |
| `\f`     | Form feed          |
| `\r`     | Carriage return    |
| `\"`     | Double quote       |
| `\\`     | Backslash          |

Any other `\` followed by an unrecognised character is an **illegal escape** —
the lexer throws `IllegalEscapeException` immediately.

A string that is opened with `"` but never closed throws `UncloseStringException`
immediately at the lexer level (not at parse time).

The surrounding double quotes are stripped at lex time — the literal's value is
the raw string content.

---

## 5. Classes

A class groups fields and methods. There is no inheritance, no abstract classes,
and no interfaces.

**Syntax:**

```
class ClassName {
    member*
}
```

Where each `member` is either a field declaration or a method declaration.
Members can appear in any order.

**Example:**

```
class Counter {
    private int count;
    public void increment() {
        count = count + 1;
    }
    public int get() {
        return count;
    }
}
```

**Rules:**
- A class name must be a valid identifier.
- No two classes in the same program may have the same name.
- Classes cannot extend other classes (no `extends` or `implements` keyword).

---

## 6. Fields

A field is a variable that belongs to an instance of a class. Every instance
of a class has its own copy of each field.

### 6.1 Mutable Fields

```
(public | private)? Type name;
(public | private)? Type name = initializer;
```

If the visibility modifier is omitted, the field defaults to **private**.

```
class Point {
    public float x;
    public float y = 0.;
    int id;           # private by default
}
```

### 6.2 Immutable Fields (`immut`)

Marking a field with `immut` makes it a constant — it cannot be re-assigned
after declaration.

```
(public | private)? immut Type name = constantExpression;
```

The initializer of an `immut` field **must be a constant expression**, meaning
it may only consist of:
- Literals
- Binary/unary operations over literals
- Other `immut` identifiers or `immut` field accesses

Runtime values (results of `new`, method calls, or `this`) are not allowed.

```
class Config {
    public immut int MAX = 100;
    private immut float PI = 3.14159;
}
```

Attempting to assign a new value to an `immut` field or variable is a
`CannotAssignToConstantException`.

---

## 7. Methods

### 7.1 Signature

```
(public | private)? ReturnType name(parameters?) {
    localDeclarations*
    statements*
}
```

- Return type is any primitive type, any class type, or `void`.
- If the visibility modifier is omitted, the method defaults to **private**.
- Method names must be unique within a class (no overloading).

### 7.2 Parameters

Parameters are listed inside the parentheses, comma-separated:

```
public int add(int a, int b) { ... }
```

- Each parameter has a type and a name.
- Parameters are locally scoped to the method.
- `immut` is not valid on parameters.

### 7.3 Method Body

A method body has two distinct sections, **in order**:

1. **Local variable declarations** — zero or more `var_decl` entries (same
   syntax as field declarations but without a visibility modifier).
2. **Statements** — zero or more statements.

This is a Pascal-style separation: **all local variables must be declared
before any statement**. You cannot declare a variable in the middle of a block.

```
public int compute(int x) {
    # --- declarations first ---
    int temp;
    float ratio = 1.5;

    # --- statements after ---
    temp = x * 2;
    return temp;
}
```

Local variables may have an optional initializer expression, which is evaluated
and assigned when the method is entered.

### 7.4 Return Rules

- A `void` method may use a bare `return;` to exit early, or simply fall off
  the end of the body.
- A non-`void` method **must** have a `return expr;` on every code path. The
  compiler rejects a non-void method that might not return a value.
- The type of the returned expression must be assignable to the declared return
  type (subject to the widening rule: `int` → `float`).

---

## 8. Statements

There are exactly three kinds of statement.

### 8.1 Assignment

```
target = expression;
```

The left-hand side can be:
- A simple variable name (`ID`) — resolves to a local, a parameter, or a
  field of `this`.
- A field access expression `object.field`.

```
count = 0;
this.x = value + 1;
node.next = new Node();
```

**Rules:**
- The right-hand side type must be assignable to the left-hand side type.
- Assigning to an `immut` variable is a compile error.

---

### 8.2 Return

```
return;           # void return
return expression;  # value return
```

- `return;` is only valid inside a `void` method.
- `return expr;` is required in non-`void` methods and the expression type
  must match the method's return type (with widening).

---

### 8.3 Method Call Statement

Calling a void method as a standalone statement:

```
expression.methodName(arguments);
```

The receiver (`expression`) must be of a class type. The method must exist,
be accessible, and must have return type `void`. Calling a non-`void` method
as a statement is a compile error — if you call a non-`void` method and
want to use the result, use a call expression inside an assignment.

```
counter.reset();
this.update(x, y);
new Logger().log("hello");
```

---

## 9. Expressions

Expressions are evaluated to produce a value. An expression can appear on the
right-hand side of an assignment, as a method argument, or as the operand of
a return statement.

### 9.1 Operator Precedence

From **lowest** to **highest** precedence:

| Level | Operators         | Associativity |
|-------|-------------------|---------------|
| 1     | `==`  `!=`        | None (no chaining) |
| 2     | `+`  `-`          | Left          |
| 3     | `*`  `/`          | Left          |
| 4     | `!`               | Right (unary) |
| 5     | Unary `+`  `-`    | Right         |
| 6     | `.name` `.name()` | Left          |
| 7     | `new`, atoms      | —             |

A parenthesised expression `(expr)` can be used at any point to override the
default precedence.

---

### 9.2 Arithmetic

```
a + b    a - b    a * b    a / b
```

- Both operands must be numeric (`int` or `float`).
- If either operand is `float`, the result is `float`; otherwise `int`.
- Integer division truncates toward zero.
- The `+` operator does **not** perform string concatenation (strings do not
  support `+` in the current version).

---

### 9.3 Equality

```
a == b    a != b
```

- Operands must be of compatible types (same type, or one widens to the other).
- Result type is always `boolean`.
- Works on primitives and object references.

---

### 9.4 Logical NOT

```
!expr
```

- Operand must be `boolean`.
- Result is `boolean`.

---

### 9.5 Unary Plus and Minus

```
+expr    -expr
```

- Operand must be `int` or `float`.
- Unary `+` is a no-op (it preserves the value).
- Unary `-` negates the value.

---

### 9.6 Member Access and Method Calls

A dot expression accesses a field or calls a method on an object:

```
object.fieldName
object.methodName(arg1, arg2)
```

**Chaining** is supported — the result of a method call can itself be dotted:

```
a.getNext().getValue()
```

The receiver must be of a class type. Accessing a field or calling a method
on a primitive type is a compile error.

**Call expression** (returns a value):
- The method must have a non-`void` return type.
- The return value becomes the value of the expression.
- Calling a `void` method inside an expression is a compile error.

---

### 9.7 Object Creation

```
new ClassName()
```

Allocates a new instance of `ClassName` on the heap. All fields are
initialised to their default values (see [Type Compatibility](#34-type-compatibility)).
No constructor arguments are supported — `new` always takes an empty argument list.

The result type of a `new` expression is `ClassName`.

---

### 9.8 `this`

Inside any method, `this` refers to the current object instance.

```
this.count = 0;
this.reset();
```

`this` has the type of the class it appears in.

---

## 10. Visibility

Every field and method has a visibility modifier: `public` or `private`.
If the modifier is omitted, the member defaults to **private**.

| Modifier  | Accessible from                           |
|-----------|-------------------------------------------|
| `public`  | Any class                                 |
| `private` | Only the class that declares the member   |

Accessing a `private` member from outside its declaring class is an
`IllegalMemberAccessException`.

---

## 11. Immutability

The `immut` keyword can be applied to field declarations and local variable
declarations:

```
immut int MAX = 100;
immut float SCALE = 2.5;
```

**Rules:**
1. An `immut` variable **must** have an initializer.
2. The initializer must be a **constant expression** — literals, unary/binary
   operations on constants, and references to other `immut` variables. `new`,
   method calls, and `this` are not constant.
3. After initialization, any attempt to assign a new value to an `immut`
   variable is a `CannotAssignToConstantException`.

`immut` cannot be applied to method parameters.

---

## 12. Scoping Rules

CPlus has three nested scopes:

```
class scope
  └─ method scope (params + locals)
```

1. **Class scope** — fields and methods of a class. Populated by the first
   compiler pass (GetEnv) so any class can reference any other class regardless
   of declaration order.

2. **Method scope** — parameters and local variables declared in a method body.
   This scope is opened when entering a method and closed when leaving it.

**Resolution order for a bare name inside a method:**
1. Method parameters
2. Local variables declared in the method body
3. Fields of `this` (the current class)

If a name is not found in any of these scopes, the compiler throws
`UndeclaredException`.

**No shadowing** — declaring a local variable or parameter with the same name
as another variable in the same scope throws `RedeclaredException`.

---

## 13. Error Reference

| Exception | Stage | Cause |
|---|---|---|
| `ErrorTokenException` | Lexer | A character that does not belong to any token |
| `UncloseStringException` | Lexer | A string literal that is never closed |
| `IllegalEscapeException` | Lexer | An unrecognised `\x` escape sequence inside a string |
| `ParseException` | Parser | Source code that violates the grammar |
| `RedeclaredException` | Semantic | A name declared more than once in the same scope |
| `UndeclaredException` | Semantic | A name used before being declared |
| `TypeMismatchInStatementException` | Semantic | Wrong type in assignment, return, or method call |
| `TypeMismatchInExpressionException` | Semantic | Wrong type in an expression (operator or argument) |
| `CannotAssignToConstantException` | Semantic | Assignment to an `immut` variable |
| `IllegalConstantExpressionException` | Semantic | Non-constant initializer for an `immut` declaration |
| `IllegalMemberAccessException` | Semantic | Accessing a `private` member from outside its class |

---

## 14. What Is Not Supported Yet

The following features are commented out in the grammar or marked as TODO in
the compiler and are **not available** in the current version:

| Feature | Notes |
|---|---|
| `&&` and `\|\|` (logical AND/OR) | Grammar rule commented out |
| `<`, `>`, `<=`, `>=` (relational operators) | Grammar rule commented out |
| `%` (modulo) and integer division | Commented out in grammar |
| `null` / `nil` | Keyword present in fragments but not active |
| Array types and indexing (`[]`) | Tokens defined but not wired into grammar |
| Constructor arguments (`new T(args)`) | Only `new T()` is supported |
| `if` / `else` statements | No grammar rule |
| `while` / `for` loops | No grammar rule |
| Static fields and methods | Placeholder AST node exists; not generated |
| Inheritance / polymorphism | Nominal typing only; no `extends` |
| Method overloading | One method per name per class |

---

## 15. Complete Example

```
class Node {
    public int value;
    public Node next;

    public void setValue(int v) {
        value = v;
    }

    public int getValue() {
        return value;
    }
}

class Stack {
    private Node top;
    private int size;

    public void push(int v) {
        Node n;
        n = new Node();
        n.setValue(v);
        n.next = top;
        top = n;
        size = size + 1;
    }

    public int pop() {
        int v;
        v = top.getValue();
        top = top.next;
        size = size - 1;
        return v;
    }

    public int getSize() {
        return size;
    }

    public boolean isEmpty() {
        return size == 0;
    }
}

class MathHelper {
    public immut float PI = 3.14159;

    public float circleArea(float r) {
        return PI * r * r;
    }

    public int square(int x) {
        return x * x;
    }
}
```

**What this example demonstrates:**
- Two classes referencing each other (`Stack` uses `Node`)
- `public` and `private` fields
- `immut` constant field
- Pascal-style method body: local declarations before statements
- `new` object creation
- Field access and method calls through `.`
- `this` fields accessed by bare name (`top`, `size`, `value`)
- All four statement types: assignment, method call statement, return with value, arithmetic expressions
