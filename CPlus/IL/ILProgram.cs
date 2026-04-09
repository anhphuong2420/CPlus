using CPlusAST;
using System.Text;

namespace CPlus.IL
{
    public class ILProgram
    {
        public List<ILClass> Classes { get; } = new();

        /// <summary>Produces a complete ilasm-compatible .il file.</summary>
        public string Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine(".assembly extern mscorlib {}");
            sb.AppendLine(".assembly CPlus {}");

            foreach (var cls in Classes)
            {
                sb.AppendLine();
                sb.Append(cls.Dump());
            }

            // Synthetic static entry point wrapping the first public void main()
            var entryClass = Classes.FirstOrDefault(
                c => c.Methods.Any(m => m.Name == "main" && m.IsPublic && m.ReturnType is VoidType));

            if (entryClass != null)
            {
                sb.AppendLine();
                sb.AppendLine(".class public EntryPoint extends [mscorlib]System.Object");
                sb.AppendLine("{");
                sb.AppendLine("    .method public static void Main() cil managed");
                sb.AppendLine("    {");
                sb.AppendLine("        .entrypoint");
                sb.AppendLine("        .maxstack 2");
                sb.AppendLine($"        newobj instance void {entryClass.Name}::.ctor()");
                sb.AppendLine($"        call instance void {entryClass.Name}::main()");
                sb.AppendLine("        ret");
                sb.AppendLine("    }");
                sb.AppendLine("}");
            }

            return sb.ToString();
        }
    }

    public class ILClass
    {
        public string Name { get; set; } = "";
        public List<ILField> Fields { get; } = new();
        public List<ILMethod> Methods { get; } = new();

        public string Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine($".class public {Name} extends [mscorlib]System.Object");
            sb.AppendLine("{");

            foreach (var f in Fields)
                sb.AppendLine($"    {f.Dump()}");

            foreach (var m in Methods)
            {
                sb.AppendLine();
                sb.Append(m.Dump());
            }

            // Auto-generated default constructor
            sb.AppendLine();
            sb.AppendLine("    .method public specialname rtspecialname instance void .ctor() cil managed");
            sb.AppendLine("    {");
            sb.AppendLine("        .maxstack 1");
            sb.AppendLine("        ldarg.0");
            sb.AppendLine("        call instance void [mscorlib]System.Object::.ctor()");
            sb.AppendLine("        ret");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }

    public class ILField
    {
        public string Name { get; set; } = "";
        public DataType Type { get; set; } = null!;
        public bool IsPublic { get; set; }
        public bool IsImmutable { get; set; }

        public string Dump()
        {
            var vis = IsPublic ? "public" : "private";
            return $".field {vis} {ILPrinter.TypeStr(Type)} {Name}";
        }
    }

    public class ILMethod
    {
        public string Name { get; set; } = "";
        public DataType ReturnType { get; set; } = null!;
        public bool IsPublic { get; set; }
        public List<ILLocal> Params { get; } = new();
        public List<ILLocal> Locals { get; } = new();
        public List<ILInstruction> Instructions { get; } = new();

        public string Dump()
        {
            var sb = new StringBuilder();
            var vis      = IsPublic ? "public" : "private";
            var paramStr = string.Join(", ", Params.Select(p => $"{ILPrinter.TypeStr(p.Type)} {p.Name}"));
            var maxStack = ILPrinter.ComputeMaxStack(Instructions);

            sb.AppendLine($"    .method {vis} instance {ILPrinter.TypeStr(ReturnType)} {Name}({paramStr}) cil managed");
            sb.AppendLine("    {");
            sb.AppendLine($"        .maxstack {maxStack}");

            if (Locals.Count > 0)
            {
                var localStr = string.Join(", ",
                    Locals.Select((l, i) => $"[{i}] {ILPrinter.TypeStr(l.Type)} {l.Name}"));
                sb.AppendLine($"        .locals init ({localStr})");
            }

            foreach (var instr in Instructions)
            {
                foreach (var line in ILPrinter.InstrLines(instr))
                {
                    // Labels get less indentation so they stand out
                    sb.AppendLine(line.EndsWith(":") ? $"    {line}" : $"        {line}");
                }
            }

            sb.AppendLine("    }");
            return sb.ToString();
        }
    }

    public class ILLocal
    {
        public string Name { get; set; } = "";
        public DataType Type { get; set; } = null!;
        public int Index { get; set; }
    }

    /// <summary>Utility for formatting IL as ilasm-compatible text.</summary>
    internal static class ILPrinter
    {
        public static string TypeStr(DataType type) => type switch
        {
            IntType      => "int32",
            FloatType    => "float32",
            BooleanType  => "bool",
            StringType   => "string",
            VoidType     => "void",
            ClassType ct => ct.ClassName.Name,
            _            => type?.GetType().Name ?? "?"
        };

        /// <summary>Expands one CPlus instruction into one or more CIL mnemonic strings.</summary>
        public static IEnumerable<string> InstrLines(ILInstruction instr)
        {
            switch (instr)
            {
                case LoadIntInstruction li:
                    yield return $"ldc.i4 {li.Value}";
                    break;

                case LoadFloatInstruction lf:
                    yield return $"ldc.r4 {lf.Value:R}";
                    break;

                case LoadBoolInstruction lb:
                    yield return lb.Value ? "ldc.i4.1" : "ldc.i4.0";
                    break;

                case LoadStrInstruction ls:
                    yield return $"ldstr \"{ls.Value}\"";
                    break;

                case IndexInstruction idx when idx.Op == Opcode.LOAD_ARG:
                    // CPlus LOAD_ARG 0 = first explicit param; CIL arg 0 = 'this', so shift +1
                    yield return $"ldarg.s {idx.Index + 1}";
                    break;

                case IndexInstruction idx when idx.Op == Opcode.STORE_ARG:
                    yield return $"starg.s {idx.Index + 1}";
                    break;

                case IndexInstruction idx when idx.Op == Opcode.LOAD_LOCAL:
                    yield return $"ldloc.s {idx.Index}";
                    break;

                case IndexInstruction idx when idx.Op == Opcode.STORE_LOCAL:
                    yield return $"stloc.s {idx.Index}";
                    break;

                case FieldInstruction fi when fi.Op == Opcode.LOAD_FIELD:
                    yield return $"ldfld {TypeStr(fi.FieldType)} {fi.OwnerClass}::{fi.FieldName}";
                    break;

                case FieldInstruction fi when fi.Op == Opcode.STORE_FIELD:
                    yield return $"stfld {TypeStr(fi.FieldType)} {fi.OwnerClass}::{fi.FieldName}";
                    break;

                case NameInstruction nm when nm.Op == Opcode.NEW:
                    yield return $"newobj instance void {nm.Name}::.ctor()";
                    break;

                case NameInstruction nm when nm.Op == Opcode.LABEL:
                    yield return $"{nm.Name}:";
                    break;

                case NameInstruction nm when nm.Op == Opcode.JUMP:
                    yield return $"br {nm.Name}";
                    break;

                case NameInstruction nm when nm.Op == Opcode.JUMP_IF_TRUE:
                    yield return $"brtrue {nm.Name}";
                    break;

                case NameInstruction nm when nm.Op == Opcode.JUMP_IF_FALSE:
                    yield return $"brfalse {nm.Name}";
                    break;

                case InvokeInstruction inv:
                    var retStr   = TypeStr(inv.ReturnType);
                    var paramStr = string.Join(", ", inv.ParamTypes.Select(TypeStr));
                    yield return $"call instance {retStr} {inv.ClassName}::{inv.MethodName}({paramStr})";
                    break;

                case SimpleInstruction si:
                    foreach (var line in SimpleOpLines(si.Op))
                        yield return line;
                    break;

                default:
                    yield return $"// {instr}";
                    break;
            }
        }

        private static IEnumerable<string> SimpleOpLines(Opcode op)
        {
            switch (op)
            {
                case Opcode.LOAD_THIS:    yield return "ldarg.0"; break;
                case Opcode.LOAD_NULL:    yield return "ldnull";  break;
                case Opcode.POP:          yield return "pop";     break;
                case Opcode.DUP:          yield return "dup";     break;
                case Opcode.RETURN:
                case Opcode.RETURN_VAL:   yield return "ret";     break;
                case Opcode.ADD:          yield return "add";     break;
                case Opcode.SUB:          yield return "sub";     break;
                case Opcode.MUL:          yield return "mul";     break;
                case Opcode.DIV:          yield return "div";     break;
                case Opcode.NEG:          yield return "neg";     break;
                case Opcode.NOT:
                    yield return "ldc.i4.0";
                    yield return "ceq";
                    break;
                case Opcode.CONCAT:
                    yield return "call string [mscorlib]System.String::Concat(string, string)";
                    break;
                case Opcode.INT_TO_FLOAT: yield return "conv.r4"; break;
                case Opcode.EQ:           yield return "ceq";     break;
                case Opcode.NEQ:
                    yield return "ceq";
                    yield return "ldc.i4.0";
                    yield return "ceq";
                    break;
                case Opcode.LT:           yield return "clt";     break;
                case Opcode.LTE:
                    yield return "cgt";
                    yield return "ldc.i4.0";
                    yield return "ceq";
                    break;
                case Opcode.GT:           yield return "cgt";     break;
                case Opcode.GTE:
                    yield return "clt";
                    yield return "ldc.i4.0";
                    yield return "ceq";
                    break;
                case Opcode.AND:          yield return "and";     break;
                case Opcode.OR:           yield return "or";      break;
                default:
                    yield return $"// unknown op {op}";
                    break;
            }
        }

        public static int ComputeMaxStack(List<ILInstruction> instructions)
        {
            int depth = 0, max = 0;
            foreach (var instr in instructions)
            {
                // For multi-expansion instructions that push before popping,
                // track the intermediate peak before applying net delta.
                if (instr is SimpleInstruction si)
                {
                    int peak = si.Op switch
                    {
                        // NOT expands to: ldc.i4.0 (+1), ceq (-1)
                        // Peak is at depth + 1 (after ldc, before ceq)
                        Opcode.NOT => depth + 1,
                        // NEQ/LTE/GTE: first op pops 2 pushes 1 (depth-1), then ldc pushes (depth),
                        // then ceq pops 2 pushes 1 (depth-1). Peak = depth (after ldc).
                        Opcode.NEQ or Opcode.LTE or Opcode.GTE => depth - 1 + 1,
                        _ => depth
                    };
                    if (peak > max) max = peak;
                }

                depth += StackDelta(instr);
                if (depth > max) max = depth;
            }
            return Math.Max(1, max);
        }

        private static int StackDelta(ILInstruction instr) => instr switch
        {
            LoadIntInstruction                                                         => +1,
            LoadFloatInstruction                                                       => +1,
            LoadBoolInstruction                                                        => +1,
            LoadStrInstruction                                                         => +1,
            IndexInstruction { Op: Opcode.LOAD_ARG or Opcode.LOAD_LOCAL }             => +1,
            IndexInstruction { Op: Opcode.STORE_ARG or Opcode.STORE_LOCAL }           => -1,
            FieldInstruction { Op: Opcode.LOAD_FIELD }                                =>  0, // pop obj, push val
            FieldInstruction { Op: Opcode.STORE_FIELD }                               => -2, // pop obj + val
            NameInstruction  { Op: Opcode.NEW }                                       => +1,
            NameInstruction  { Op: Opcode.LABEL or Opcode.JUMP }                     =>  0,
            NameInstruction  { Op: Opcode.JUMP_IF_TRUE or Opcode.JUMP_IF_FALSE }     => -1,
            InvokeInstruction inv => (inv.Op == Opcode.INVOKE ? 1 : 0) - 1 - inv.ArgCount,
            SimpleInstruction si  => SimpleStackDelta(si.Op),
            _                                                                         =>  0,
        };

        private static int SimpleStackDelta(Opcode op) => op switch
        {
            Opcode.LOAD_THIS                                                        => +1,
            Opcode.LOAD_NULL                                                        => +1,
            Opcode.POP                                                              => -1,
            Opcode.DUP                                                              => +1,
            Opcode.RETURN                                                           =>  0,
            Opcode.RETURN_VAL                                                       => -1,
            Opcode.ADD or Opcode.SUB or Opcode.MUL or Opcode.DIV                   => -1,
            Opcode.NEG                                                              =>  0,
            Opcode.NOT                                                              =>  0, // net: pop 1, push 1
            Opcode.CONCAT                                                           => -1,
            Opcode.INT_TO_FLOAT                                                     =>  0,
            Opcode.EQ or Opcode.NEQ or Opcode.LT or Opcode.LTE
                or Opcode.GT or Opcode.GTE                                          => -1,
            Opcode.AND or Opcode.OR                                                 => -1,
            _                                                                       =>  0,
        };
    }
}
