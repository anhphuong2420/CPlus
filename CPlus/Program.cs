using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using CPlus;
using CPlus.Exceptions;
using CPlus.IL;
using CPlus.SematicChecker;
using CPlusAST;

public class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: cplus <file.cplus>");
            return 1;
        }

        var filePath = args[0];
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: '{filePath}' not found.");
            return 1;
        }

        try
        {
            var ast       = Parse(filePath);
            var env       = Analyse(ast);
            var ilProgram = GenerateIL(ast, env);

            Console.WriteLine(ilProgram.Dump());
            Console.WriteLine("Compiled successfully.");
            return 0;
        }
        catch (CplusSyntaxException e)
        {
            Console.Error.WriteLine($"[Syntax]   {e.Message}");
            return 1;
        }
        catch (CplusStaticException e)
        {
            Console.Error.WriteLine($"[Semantic] {e.Message}");
            return 1;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Internal] {e}");
            return 1;
        }
    }

    // -----------------------------------------------------------------------
    // Pipeline stages
    // -----------------------------------------------------------------------

    private static CPlusAST.Program Parse(string filePath)
    {
        var inputStream = new AntlrFileStream(filePath);
        var lexer       = new CPlusLexer(inputStream);
        var tokens      = new CommonTokenStream(lexer);
        var parser      = new CPlusParser(tokens) { ErrorHandler = new StrictErrorStrategy() };
        var visitor     = new CPlusASTVisitor();
        return (CPlusAST.Program)visitor.Visit(parser.program());
    }

    private static CompileEnviroment Analyse(CPlusAST.Program ast)
    {
        var env = new CompileEnviroment();
        new SematicCheckerAuto().Visit(ast, env);
        return env;
    }

    private static ILProgram GenerateIL(CPlusAST.Program ast, CompileEnviroment env)
    {
        return new ILGenerator().Generate(ast, env);
    }
}
