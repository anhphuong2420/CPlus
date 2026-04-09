using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using CPlus;
using CPlus.IL;
using CPlus.SematicChecker;
using CPlusAST;
using FluentAssertions;

namespace CPlusTest
{
    public class TestIL
    {
        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static ILProgram CompileToIL(string filePath)
        {
            var inputStream = new AntlrFileStream(filePath);
            var lexer       = new CPlusLexer(inputStream);
            var tokens      = new CommonTokenStream(lexer);
            var parser      = new CPlusParser(tokens) { ErrorHandler = new StrictErrorStrategy() };
            var visitor     = new CPlusASTVisitor();
            var ast         = (CPlusAST.Program)visitor.Visit(parser.program());

            var env = new CompileEnviroment();
            new SematicCheckerAuto().Visit(ast, env);

            return new ILGenerator().Generate(ast, env);
        }

        private static List<Opcode> Opcodes(ILMethod method) =>
            method.Instructions.Select(i => i.Op).ToList();

        // -----------------------------------------------------------------------
        // Structure
        // -----------------------------------------------------------------------

        [Test]
        public void Counter_ClassStructure()
        {
            var il = CompileToIL("./ILTests/counter.cplus");

            il.Classes.Should().HaveCount(1);
            var cls = il.Classes[0];
            cls.Name.Should().Be("Counter");

            cls.Fields.Should().HaveCount(1);
            cls.Fields[0].Name.Should().Be("count");
            cls.Fields[0].IsPublic.Should().BeFalse();
            cls.Fields[0].IsImmutable.Should().BeFalse();

            var methodNames = cls.Methods.Select(m => m.Name).ToList();
            methodNames.Should().Contain("increment").And.Contain("get").And.Contain("main");
        }

        // -----------------------------------------------------------------------
        // increment(int amount) : void
        //   count = count + amount;
        //
        // Expected IL:
        //   LOAD_THIS               ← receiver for STORE_FIELD
        //   LOAD_THIS               ← load 'count' field of this
        //   LOAD_FIELD count
        //   LOAD_ARG 0              ← load 'amount'
        //   ADD
        //   STORE_FIELD count
        //   RETURN
        // -----------------------------------------------------------------------

        [Test]
        public void Counter_Increment_OpcodeSequence()
        {
            var il     = CompileToIL("./ILTests/counter.cplus");
            var method = il.Classes[0].Methods.First(m => m.Name == "increment");

            method.Params.Should().HaveCount(1);
            method.Params[0].Name.Should().Be("amount");
            method.Locals.Should().BeEmpty();

            Opcodes(method).Should().Equal(
                Opcode.LOAD_THIS,
                Opcode.LOAD_THIS,
                Opcode.LOAD_FIELD,
                Opcode.LOAD_ARG,
                Opcode.ADD,
                Opcode.STORE_FIELD,
                Opcode.RETURN);
        }

        [Test]
        public void Counter_Increment_Operands()
        {
            var il     = CompileToIL("./ILTests/counter.cplus");
            var instrs = il.Classes[0].Methods.First(m => m.Name == "increment").Instructions;

            var loadField = instrs[2].Should().BeOfType<FieldInstruction>().Which;
            loadField.Op.Should().Be(Opcode.LOAD_FIELD);
            loadField.OwnerClass.Should().Be("Counter");
            loadField.FieldName.Should().Be("count");

            instrs[3].Should().Be(new IndexInstruction(Opcode.LOAD_ARG, 0));

            var storeField = instrs[5].Should().BeOfType<FieldInstruction>().Which;
            storeField.Op.Should().Be(Opcode.STORE_FIELD);
            storeField.OwnerClass.Should().Be("Counter");
            storeField.FieldName.Should().Be("count");
        }

        // -----------------------------------------------------------------------
        // get() : int
        //   return count;
        //
        // Expected IL:
        //   LOAD_THIS
        //   LOAD_FIELD count
        //   RETURN_VAL
        // -----------------------------------------------------------------------

        [Test]
        public void Counter_Get_OpcodeSequence()
        {
            var il     = CompileToIL("./ILTests/counter.cplus");
            var method = il.Classes[0].Methods.First(m => m.Name == "get");

            method.Params.Should().BeEmpty();
            method.Locals.Should().BeEmpty();

            Opcodes(method).Should().Equal(
                Opcode.LOAD_THIS,
                Opcode.LOAD_FIELD,
                Opcode.RETURN_VAL);
        }

        [Test]
        public void Counter_Get_Operands()
        {
            var il     = CompileToIL("./ILTests/counter.cplus");
            var instrs = il.Classes[0].Methods.First(m => m.Name == "get").Instructions;

            var loadField = instrs[1].Should().BeOfType<FieldInstruction>().Which;
            loadField.Op.Should().Be(Opcode.LOAD_FIELD);
            loadField.OwnerClass.Should().Be("Counter");
            loadField.FieldName.Should().Be("count");
        }

        // -----------------------------------------------------------------------
        // main() : void
        //   this.increment(1);
        //
        // Expected IL:
        //   LOAD_THIS                ← receiver
        //   LOAD_CONST_INT 1         ← argument
        //   INVOKE_VOID Counter.increment (1 arg)
        //   RETURN
        // -----------------------------------------------------------------------

        [Test]
        public void Counter_Main_OpcodeSequence()
        {
            var il     = CompileToIL("./ILTests/counter.cplus");
            var method = il.Classes[0].Methods.First(m => m.Name == "main");

            method.Params.Should().BeEmpty();
            method.Locals.Should().BeEmpty();

            Opcodes(method).Should().Equal(
                Opcode.LOAD_THIS,
                Opcode.LOAD_CONST_INT,
                Opcode.INVOKE_VOID,
                Opcode.RETURN);
        }

        [Test]
        public void Counter_Main_Operands()
        {
            var il     = CompileToIL("./ILTests/counter.cplus");
            var instrs = il.Classes[0].Methods.First(m => m.Name == "main").Instructions;

            instrs[1].Should().Be(new LoadIntInstruction(1));

            var invoke = instrs[2].Should().BeOfType<InvokeInstruction>().Which;
            invoke.Op.Should().Be(Opcode.INVOKE_VOID);
            invoke.ClassName.Should().Be("Counter");
            invoke.MethodName.Should().Be("increment");
            invoke.ArgCount.Should().Be(1);
        }
    }
}
