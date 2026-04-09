using CPlusAST;
using System.Text;

namespace CPlus.IL
{
    public class ILProgram
    {
        public List<ILClass> Classes { get; } = new();

        public string Dump()
        {
            var sb = new StringBuilder();
            foreach (var cls in Classes)
            {
                sb.AppendLine(cls.Dump());
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
            sb.AppendLine($".class {Name}");

            foreach (var f in Fields)
                sb.AppendLine($"    {f.Dump()}");

            foreach (var m in Methods)
            {
                sb.AppendLine();
                sb.Append(m.Dump());
            }

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
            var imm = IsImmutable ? " immut" : "";
            return $".field {vis}{imm} {ILPrinter.TypeStr(Type)} {Name}";
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
            var vis = IsPublic ? "public" : "private";
            var paramStr = string.Join(", ", Params.Select(p => $"{ILPrinter.TypeStr(p.Type)} {p.Name}"));
            sb.AppendLine($"    .method {vis} {ILPrinter.TypeStr(ReturnType)} {Name}({paramStr})");

            if (Locals.Count > 0)
            {
                var localStr = string.Join(", ", Locals.Select(l => $"{ILPrinter.TypeStr(l.Type)} {l.Name}"));
                sb.AppendLine($"        .locals ({localStr})");
            }

            for (int i = 0; i < Instructions.Count; i++)
            {
                sb.AppendLine($"        {i:D3}: {ILPrinter.InstrStr(Instructions[i])}");
            }

            return sb.ToString();
        }
    }

    public class ILLocal
    {
        public string Name { get; set; } = "";
        public DataType Type { get; set; } = null!;
        public int Index { get; set; }
    }

    /// <summary>Utility for formatting IL as human-readable text.</summary>
    internal static class ILPrinter
    {
        public static string TypeStr(DataType type) => type switch
        {
            IntType     => "int",
            FloatType   => "float",
            BooleanType => "boolean",
            StringType  => "string",
            VoidType    => "void",
            ClassType ct => ct.ClassName.Name,
            _ => type?.GetType().Name ?? "?"
        };

        public static string InstrStr(ILInstruction instr) => instr switch
        {
            LoadIntInstruction   li  => $"LOAD_CONST_INT    {li.Value}",
            LoadFloatInstruction lf  => $"LOAD_CONST_FLOAT  {lf.Value}",
            LoadBoolInstruction  lb  => $"LOAD_CONST_BOOL   {lb.Value}",
            LoadStrInstruction   ls  => $"LOAD_CONST_STR    \"{ls.Value}\"",
            IndexInstruction     idx => $"{idx.Op,-16} {idx.Index}",
            NameInstruction      nm  => $"{nm.Op,-16} {nm.Name}",
            InvokeInstruction    inv => $"{inv.Op,-16} {inv.ClassName}.{inv.MethodName}  ({inv.ArgCount} args)",
            SimpleInstruction    si  => $"{si.Op}",
            _ => instr.ToString()!
        };
    }
}
