using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace Lucent.Visualizer.MilkDrop;

/// <summary>A compiled block of MilkDrop equations, ready to run against its context's variables.</summary>
public sealed class EelCode
{
    private readonly Func<double[], double> _run;
    private readonly EelContext _context;

    internal EelCode(Func<double[], double> run, EelContext context)
    {
        _run = run;
        _context = context;
    }

    /// <summary>Code that does nothing, for a block that's empty or didn't compile.</summary>
    public static EelCode Empty(EelContext context) => new(_ => 0, context);

    /// <summary>Runs the code and returns the value of its last statement.</summary>
    public double Run() => _run(_context.Values);
}

/// <summary>Raised for code the compiler can't make sense of, with what it found and roughly where.</summary>
public sealed class EelException : Exception
{
    public EelException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Compiles MilkDrop's expression language (ns-eel2, as MilkDrop 2 and projectM use it) into .NET
/// delegates with System.Linq.Expressions, so equations that run for every point of the warp mesh
/// every frame cost about as much as hand-written C#. Statements are separated by semicolons;
/// variables need no declaring and start at 0; + - * / % ^ (power), comparisons, &amp;&amp; || ! ?:
/// and the usual functions (above, below, equal, if, sqr, sqrt, sin..., rand, int, sigmoid, band,
/// bor, bnot, megabuf, gmegabuf, loop, while...) are all there. Division and modulo by zero give 0,
/// as they do in MilkDrop, so a preset can't stop on a stray divide.
/// </summary>
public sealed class EelCompiler
{
    // ns-eel2 counts anything within this of zero as false, and numbers this close as equal.
    private const double CloseEnough = 0.00001;
    private const int MaxLoopCount = 1048576;

    private static readonly MethodInfo MemoryGet = typeof(EelMemory).GetMethod(nameof(EelMemory.Get))!;
    private static readonly MethodInfo MemorySet = typeof(EelMemory).GetMethod(nameof(EelMemory.Set))!;

    private readonly EelContext _context;
    private readonly ParameterExpression _values = Expression.Parameter(typeof(double[]), "values");
    private readonly List<string> _warnings;
    private List<Token> _tokens = new();
    private int _at;

    private EelCompiler(EelContext context, List<string> warnings)
    {
        _context = context;
        _warnings = warnings;
    }

    /// <summary>
    /// Compiles <paramref name="source"/> to run against <paramref name="context"/>. Throws
    /// <see cref="EelException"/> if it doesn't parse; things it can only half-understand (an
    /// unknown function, say) compile anyway and are described in <paramref name="warnings"/>.
    /// </summary>
    public static EelCode Compile(string source, EelContext context, List<string>? warnings = null)
    {
        var compiler = new EelCompiler(context, warnings ?? new List<string>());
        compiler._tokens = Lex(source);
        compiler._at = 0;
        var tree = compiler.ParseSequence(TokenKind.End);
        compiler.Expect(TokenKind.End);
        var body = compiler.Generate(tree);
        var lambda = Expression.Lambda<Func<double[], double>>(body, compiler._values);
        return new EelCode(lambda.Compile(), context);
    }

    // ---- Reading the text ----

    private enum TokenKind
    {
        Number,
        Name,
        Operator,
        OpenParen,
        CloseParen,
        OpenBracket,
        CloseBracket,
        Comma,
        Semicolon,
        Question,
        Colon,
        End,
    }

    private readonly record struct Token(TokenKind Kind, string Text, double Value, int Position);

    private static readonly string[] Operators =
    {
        "===", "!==", "==", "!=", "<=", ">=", "&&", "||", "+=", "-=", "*=", "/=", "%=", "^=", "|=", "&=",
        "+", "-", "*", "/", "%", "^", "<", ">", "=", "!", "|", "&", "~",
    };

    private static List<Token> Lex(string source)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? source.Length : end + 2;
                continue;
            }

            var start = i;
            if (char.IsDigit(c) || (c == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1])))
            {
                tokens.Add(new Token(TokenKind.Number, string.Empty, ReadNumber(source, ref i), start));
                continue;
            }

            if (c == '$')
            {
                // $pi, $e and $phi; anything else after a $ is taken as 0.
                i++;
                var nameStart = i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                {
                    i++;
                }

                var constant = source[nameStart..i].ToLowerInvariant() switch
                {
                    "pi" => Math.PI,
                    "e" => Math.E,
                    "phi" => 1.61803398874989484820,
                    _ => 0.0,
                };
                tokens.Add(new Token(TokenKind.Number, string.Empty, constant, start));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_' || source[i] == '.'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Name, source[start..i].ToLowerInvariant(), 0, start));
                continue;
            }

            var single = c switch
            {
                '(' => TokenKind.OpenParen,
                ')' => TokenKind.CloseParen,
                '[' => TokenKind.OpenBracket,
                ']' => TokenKind.CloseBracket,
                ',' => TokenKind.Comma,
                ';' => TokenKind.Semicolon,
                '?' => TokenKind.Question,
                ':' => TokenKind.Colon,
                _ => TokenKind.End,
            };
            if (single != TokenKind.End)
            {
                tokens.Add(new Token(single, c.ToString(), 0, start));
                i++;
                continue;
            }

            var op = Operators.FirstOrDefault(candidate => string.CompareOrdinal(source, i, candidate, 0, candidate.Length) == 0);
            if (op is null)
            {
                throw new EelException($"Unexpected '{c}' at character {i + 1}.");
            }

            tokens.Add(new Token(TokenKind.Operator, op, 0, start));
            i += op.Length;
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, 0, source.Length));
        return tokens;
    }

    private static double ReadNumber(string source, ref int i)
    {
        var start = i;
        if (source[i] == '0' && i + 1 < source.Length && (source[i + 1] == 'x' || source[i + 1] == 'X'))
        {
            i += 2;
            var hexStart = i;
            while (i < source.Length && Uri.IsHexDigit(source[i]))
            {
                i++;
            }

            return i > hexStart ? long.Parse(source[hexStart..i], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0;
        }

        while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.'))
        {
            i++;
        }

        if (i < source.Length && (source[i] == 'e' || source[i] == 'E'))
        {
            var exponent = i + 1;
            if (exponent < source.Length && (source[exponent] == '+' || source[exponent] == '-'))
            {
                exponent++;
            }

            if (exponent < source.Length && char.IsDigit(source[exponent]))
            {
                i = exponent;
                while (i < source.Length && char.IsDigit(source[i]))
                {
                    i++;
                }
            }
        }

        // "1.2.3" and the like: read as much as makes a number.
        var text = source[start..i];
        while (text.Length > 0 && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            text = text[..^1];
        }

        return text.Length == 0 ? 0 : double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // ---- Parsing ----

    private abstract record Node;

    private sealed record NumberNode(double Value) : Node;

    private sealed record VariableNode(string Name) : Node;

    private sealed record CallNode(string Name, List<Node> Arguments) : Node;

    private sealed record IndexNode(Node Base, Node Offset) : Node;

    private sealed record UnaryNode(string Operator, Node Operand) : Node;

    private sealed record BinaryNode(string Operator, Node Left, Node Right) : Node;

    private sealed record AssignNode(string Operator, Node Target, Node Value) : Node;

    private sealed record ConditionNode(Node Test, Node IfTrue, Node IfFalse) : Node;

    private sealed record SequenceNode(List<Node> Statements) : Node;

    private Token Peek => _tokens[_at];

    private Token Next() => _tokens[_at++];

    private bool IsOperator(params string[] ops) => Peek.Kind == TokenKind.Operator && ops.Contains(Peek.Text);

    private void Expect(TokenKind kind)
    {
        if (Peek.Kind != kind)
        {
            throw new EelException($"Expected {Describe(kind)} at character {Peek.Position + 1}, found {DescribeToken(Peek)}.");
        }

        _at++;
    }

    private static string Describe(TokenKind kind) => kind switch
    {
        TokenKind.CloseParen => "')'",
        TokenKind.CloseBracket => "']'",
        TokenKind.Colon => "':'",
        TokenKind.End => "the end of the code",
        _ => kind.ToString(),
    };

    private static string DescribeToken(Token token) => token.Kind switch
    {
        TokenKind.End => "the end of the code",
        TokenKind.Number => token.Value.ToString(CultureInfo.InvariantCulture),
        _ => $"'{token.Text}'",
    };

    /// <summary>Statements separated by semicolons, up to (not including) one of the closing tokens.</summary>
    private Node ParseSequence(params TokenKind[] closers)
    {
        var statements = new List<Node>();
        while (true)
        {
            if (Peek.Kind == TokenKind.Semicolon)
            {
                _at++;
                continue;
            }

            if (closers.Contains(Peek.Kind) || Peek.Kind == TokenKind.End)
            {
                break;
            }

            var before = _at;
            statements.Add(ParseAssignment());
            if (_at == before)
            {
                throw new EelException($"Unexpected {DescribeToken(Peek)} at character {Peek.Position + 1}.");
            }

            // A missing semicolon between two statements (common where a preset's lines were
            // joined) starts the next statement anyway, as MilkDrop's own loader is forgiving too.
        }

        return statements.Count == 1 ? statements[0] : new SequenceNode(statements);
    }

    private Node ParseAssignment()
    {
        var target = ParseConditional();
        if (IsOperator("=", "+=", "-=", "*=", "/=", "%=", "^=", "|=", "&="))
        {
            var op = Next().Text;
            var value = ParseAssignment();
            return new AssignNode(op, target, value);
        }

        return target;
    }

    private Node ParseConditional()
    {
        var test = ParseLogicalOr();
        if (Peek.Kind != TokenKind.Question)
        {
            return test;
        }

        _at++;
        var ifTrue = ParseAssignment();
        Node ifFalse = new NumberNode(0);
        if (Peek.Kind == TokenKind.Colon)
        {
            _at++;
            ifFalse = ParseAssignment();
        }

        return new ConditionNode(test, ifTrue, ifFalse);
    }

    private Node ParseLogicalOr()
    {
        var left = ParseLogicalAnd();
        while (IsOperator("||"))
        {
            _at++;
            left = new BinaryNode("||", left, ParseLogicalAnd());
        }

        return left;
    }

    private Node ParseLogicalAnd()
    {
        var left = ParseComparison();
        while (IsOperator("&&"))
        {
            _at++;
            left = new BinaryNode("&&", left, ParseComparison());
        }

        return left;
    }

    private Node ParseComparison()
    {
        var left = ParseBitwise();
        while (IsOperator("==", "===", "!=", "!==", "<", ">", "<=", ">="))
        {
            var op = Next().Text;
            left = new BinaryNode(op, left, ParseBitwise());
        }

        return left;
    }

    private Node ParseBitwise()
    {
        var left = ParseAdditive();
        while (IsOperator("|", "&", "~"))
        {
            var op = Next().Text;
            left = new BinaryNode(op, left, ParseAdditive());
        }

        return left;
    }

    private Node ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (IsOperator("+", "-"))
        {
            var op = Next().Text;
            left = new BinaryNode(op, left, ParseMultiplicative());
        }

        return left;
    }

    private Node ParseMultiplicative()
    {
        var left = ParsePower();
        while (IsOperator("*", "/", "%"))
        {
            var op = Next().Text;
            left = new BinaryNode(op, left, ParsePower());
        }

        return left;
    }

    private Node ParsePower()
    {
        var left = ParseUnary();
        while (IsOperator("^"))
        {
            _at++;
            left = new BinaryNode("^", left, ParseUnary());
        }

        return left;
    }

    private Node ParseUnary()
    {
        if (IsOperator("-", "+", "!"))
        {
            var op = Next().Text;
            return new UnaryNode(op, ParseUnary());
        }

        return ParsePostfix();
    }

    private Node ParsePostfix()
    {
        var node = ParsePrimary();
        while (Peek.Kind == TokenKind.OpenBracket)
        {
            _at++;
            var offset = Peek.Kind == TokenKind.CloseBracket ? new NumberNode(0) : ParseSequence(TokenKind.CloseBracket);
            Expect(TokenKind.CloseBracket);
            node = new IndexNode(node, offset);
        }

        return node;
    }

    private Node ParsePrimary()
    {
        var token = Peek;
        switch (token.Kind)
        {
            case TokenKind.Number:
                _at++;
                return new NumberNode(token.Value);
            case TokenKind.OpenParen:
            {
                _at++;
                var inner = ParseSequence(TokenKind.CloseParen);
                Expect(TokenKind.CloseParen);
                return inner;
            }

            case TokenKind.Name:
            {
                _at++;
                if (Peek.Kind != TokenKind.OpenParen)
                {
                    return new VariableNode(token.Text);
                }

                _at++;
                var arguments = new List<Node>();
                while (Peek.Kind != TokenKind.CloseParen)
                {
                    arguments.Add(ParseSequence(TokenKind.Comma, TokenKind.CloseParen));
                    if (Peek.Kind == TokenKind.Comma)
                    {
                        _at++;
                    }
                    else
                    {
                        break;
                    }
                }

                Expect(TokenKind.CloseParen);
                return new CallNode(token.Text, arguments);
            }

            default:
                throw new EelException($"Unexpected {DescribeToken(token)} at character {token.Position + 1}.");
        }
    }

    // ---- Making the code ----

    private Expression Generate(Node node) => node switch
    {
        NumberNode number => Expression.Constant(number.Value),
        VariableNode variable => Variable(variable.Name),
        SequenceNode sequence => sequence.Statements.Count == 0
            ? Expression.Constant(0.0)
            : Expression.Block(typeof(double), sequence.Statements.Select(Generate)),
        UnaryNode unary => unary.Operator switch
        {
            "-" => Expression.Negate(Generate(unary.Operand)),
            "!" => Expression.Condition(Truth(Generate(unary.Operand)), Expression.Constant(0.0), Expression.Constant(1.0)),
            _ => Generate(unary.Operand),
        },
        BinaryNode binary => Binary(binary.Operator, binary.Left, binary.Right),
        ConditionNode condition => Expression.Condition(Truth(Generate(condition.Test)), Generate(condition.IfTrue), Generate(condition.IfFalse)),
        AssignNode assign => Assign(assign.Operator, assign.Target, assign.Value),
        IndexNode index => Expression.Call(
            Expression.Constant(_context.Megabuf), MemoryGet, Expression.Add(Generate(index.Base), Generate(index.Offset))),
        CallNode call => Call(call),
        _ => throw new EelException("Unknown expression."),
    };

    private Expression Variable(string name)
    {
        // reg00 to reg99 are shared by every preset, the rest belong to this block's context.
        if (name.Length == 5 && name.StartsWith("reg", StringComparison.Ordinal) && char.IsDigit(name[3]) && char.IsDigit(name[4]))
        {
            var register = (name[3] - '0') * 10 + (name[4] - '0');
            return Expression.ArrayAccess(Expression.Constant(_context.Globals.Registers), Expression.Constant(register));
        }

        return Expression.ArrayAccess(_values, Expression.Constant(_context.Index(name)));
    }

    private static Expression Truth(Expression value) =>
        Expression.GreaterThan(Expression.Call(typeof(Math).GetMethod(nameof(Math.Abs), new[] { typeof(double) })!, value), Expression.Constant(CloseEnough));

    private static Expression FromBool(Expression test) =>
        Expression.Condition(test, Expression.Constant(1.0), Expression.Constant(0.0));

    private Expression Binary(string op, Node leftNode, Node rightNode)
    {
        if (op is "&&" or "||")
        {
            // Both short-circuit, like C.
            var leftTest = Truth(Generate(leftNode));
            var rightTest = Truth(Generate(rightNode));
            return FromBool(op == "&&" ? Expression.AndAlso(leftTest, rightTest) : Expression.OrElse(leftTest, rightTest));
        }

        var left = Generate(leftNode);
        var right = Generate(rightNode);
        return Operate(op, left, right);
    }

    private static Expression Operate(string op, Expression left, Expression right) => op switch
    {
        "+" => Expression.Add(left, right),
        "-" => Expression.Subtract(left, right),
        "*" => Expression.Multiply(left, right),
        "/" => Helper(nameof(EelFunctions.Divide), left, right),
        "%" => Helper(nameof(EelFunctions.Modulo), left, right),
        "^" => Helper(nameof(EelFunctions.Power), left, right),
        "|" => Helper(nameof(EelFunctions.BitOr), left, right),
        "&" => Helper(nameof(EelFunctions.BitAnd), left, right),
        "~" => Helper(nameof(EelFunctions.BitXor), left, right),
        "==" or "===" => Helper(nameof(EelFunctions.Equal), left, right),
        "!=" or "!==" => Expression.Subtract(Expression.Constant(1.0), Helper(nameof(EelFunctions.Equal), left, right)),
        "<" => FromBool(Expression.LessThan(left, right)),
        ">" => FromBool(Expression.GreaterThan(left, right)),
        "<=" => FromBool(Expression.LessThanOrEqual(left, right)),
        ">=" => FromBool(Expression.GreaterThanOrEqual(left, right)),
        _ => throw new EelException($"Unknown operator '{op}'."),
    };

    private Expression Assign(string op, Node targetNode, Node valueNode)
    {
        var value = Generate(valueNode);
        var compound = op.Length == 2 ? op[..1] : null;
        switch (targetNode)
        {
            case VariableNode variable:
            {
                var target = Variable(variable.Name);
                return Expression.Assign(target, compound is null ? value : Operate(compound, target, value));
            }

            case CallNode { Name: "megabuf" or "gmegabuf", Arguments.Count: 1 } call:
                return AssignMemory(call.Name == "megabuf" ? _context.Megabuf : _context.Globals.Megabuf, Generate(call.Arguments[0]), compound, value);
            case IndexNode index:
                return AssignMemory(_context.Megabuf, Expression.Add(Generate(index.Base), Generate(index.Offset)), compound, value);
            default:
                // Assigning to something that isn't a variable: MilkDrop ignores the assignment.
                _warnings.Add("Assigned to something that isn't a variable; the assignment is ignored.");
                return value;
        }
    }

    private static Expression AssignMemory(EelMemory memory, Expression at, string? compound, Expression value)
    {
        var slot = Expression.Variable(typeof(double), "slot");
        var buffer = Expression.Constant(memory);
        var newValue = compound is null ? value : Operate(compound, Expression.Call(buffer, MemoryGet, slot), value);
        return Expression.Block(typeof(double), new[] { slot },
            Expression.Assign(slot, at),
            Expression.Call(buffer, MemorySet, slot, newValue));
    }

    private Expression Call(CallNode call)
    {
        var args = call.Arguments;
        switch (call.Name)
        {
            case "if" when args.Count == 3:
                return Expression.Condition(Truth(Generate(args[0])), Generate(args[1]), Generate(args[2]));
            case "megabuf" when args.Count == 1:
                return Expression.Call(Expression.Constant(_context.Megabuf), MemoryGet, Generate(args[0]));
            case "gmegabuf" when args.Count == 1:
                return Expression.Call(Expression.Constant(_context.Globals.Megabuf), MemoryGet, Generate(args[0]));
            case "assign" when args.Count == 2:
                return Assign("=", args[0], args[1]);
            case "exec2" or "exec3" when args.Count >= 1:
                return Expression.Block(typeof(double), args.Select(Generate));
            case "loop" when args.Count == 2:
                return Loop(Generate(args[0]), Generate(args[1]));
            case "while" when args.Count == 1:
                return While(Generate(args[0]));
            case "freembuf" or "memset" or "memcpy":
                // Memory management MilkDrop presets rarely need: run the arguments and carry on.
                return Expression.Block(typeof(double), args.Select(Generate).Append(Expression.Constant(0.0)));
        }

        if (_context.Functions.TryGetValue(call.Name, out var extra))
        {
            // A function the host adds (AVS's getosc and getspec, say), called with its numbers.
            var count = extra.Method.GetParameters().Length;
            if (args.Count != count)
            {
                _warnings.Add($"'{call.Name}' takes {count} value(s) but was given {args.Count}.");
            }

            var extraValues = Enumerable.Range(0, count)
                .Select(i => i < args.Count ? Generate(args[i]) : Expression.Constant(0.0))
                .ToArray();
            return Expression.Invoke(Expression.Constant(extra), extraValues);
        }

        if (!Functions.TryGetValue(call.Name, out var function))
        {
            _warnings.Add($"Unknown function '{call.Name}' is treated as 0.");
            return Expression.Block(typeof(double), args.Select(Generate).Append(Expression.Constant(0.0)));
        }

        var parameters = function.GetParameters().Length;
        if (args.Count != parameters)
        {
            _warnings.Add($"'{call.Name}' takes {parameters} value(s) but was given {args.Count}.");
        }

        var values = Enumerable.Range(0, parameters)
            .Select(i => i < args.Count ? Generate(args[i]) : Expression.Constant(0.0))
            .ToArray();
        return Expression.Call(function, values);
    }

    private static Expression Loop(Expression count, Expression body)
    {
        var remaining = Expression.Variable(typeof(int), "remaining");
        var done = Expression.Label("done");
        return Expression.Block(typeof(double), new[] { remaining },
            Expression.Assign(remaining, Expression.Call(typeof(EelFunctions).GetMethod(nameof(EelFunctions.LoopCount))!, count, Expression.Constant(MaxLoopCount))),
            Expression.Loop(
                Expression.IfThenElse(
                    Expression.GreaterThan(remaining, Expression.Constant(0)),
                    Expression.Block(body, Expression.PostDecrementAssign(remaining)),
                    Expression.Break(done)),
                done),
            Expression.Constant(0.0));
    }

    private static Expression While(Expression body)
    {
        // Runs the body until it gives 0, but never forever.
        var count = Expression.Variable(typeof(int), "count");
        var done = Expression.Label("done");
        return Expression.Block(typeof(double), new[] { count },
            Expression.Assign(count, Expression.Constant(0)),
            Expression.Loop(
                Expression.IfThenElse(
                    Expression.AndAlso(Expression.LessThan(Expression.PostIncrementAssign(count), Expression.Constant(MaxLoopCount)), Truth(body)),
                    Expression.Empty(),
                    Expression.Break(done)),
                done),
            Expression.Constant(0.0));
    }

    private static Expression Helper(string name, params Expression[] args) =>
        Expression.Call(typeof(EelFunctions).GetMethod(name)!, args);

    private static readonly Dictionary<string, MethodInfo> Functions = BuildFunctions();

    private static Dictionary<string, MethodInfo> BuildFunctions()
    {
        var helpers = typeof(EelFunctions);
        MethodInfo Of(string name) => helpers.GetMethod(name)!;
        MethodInfo MathOf(string name, int parameters) =>
            typeof(Math).GetMethod(name, Enumerable.Repeat(typeof(double), parameters).ToArray())!;
        return new Dictionary<string, MethodInfo>(StringComparer.Ordinal)
        {
            ["sin"] = MathOf(nameof(Math.Sin), 1),
            ["cos"] = MathOf(nameof(Math.Cos), 1),
            ["tan"] = MathOf(nameof(Math.Tan), 1),
            ["asin"] = MathOf(nameof(Math.Asin), 1),
            ["acos"] = MathOf(nameof(Math.Acos), 1),
            ["atan"] = MathOf(nameof(Math.Atan), 1),
            ["atan2"] = MathOf(nameof(Math.Atan2), 2),
            ["exp"] = MathOf(nameof(Math.Exp), 1),
            ["floor"] = MathOf(nameof(Math.Floor), 1),
            ["ceil"] = MathOf(nameof(Math.Ceiling), 1),
            ["abs"] = MathOf(nameof(Math.Abs), 1),
            ["min"] = MathOf(nameof(Math.Min), 2),
            ["max"] = MathOf(nameof(Math.Max), 2),
            ["sqr"] = Of(nameof(EelFunctions.Square)),
            ["sqrt"] = Of(nameof(EelFunctions.SquareRoot)),
            ["invsqrt"] = Of(nameof(EelFunctions.InverseSquareRoot)),
            ["pow"] = Of(nameof(EelFunctions.Power)),
            ["log"] = Of(nameof(EelFunctions.Log)),
            ["log10"] = Of(nameof(EelFunctions.Log10)),
            ["sign"] = Of(nameof(EelFunctions.Sign)),
            ["rand"] = Of(nameof(EelFunctions.Random)),
            ["int"] = MathOf(nameof(Math.Floor), 1),
            ["sigmoid"] = Of(nameof(EelFunctions.Sigmoid)),
            ["above"] = Of(nameof(EelFunctions.Above)),
            ["below"] = Of(nameof(EelFunctions.Below)),
            ["equal"] = Of(nameof(EelFunctions.Equal)),
            ["band"] = Of(nameof(EelFunctions.LogicalAnd)),
            ["bor"] = Of(nameof(EelFunctions.LogicalOr)),
            ["bnot"] = Of(nameof(EelFunctions.LogicalNot)),
            ["fmod"] = Of(nameof(EelFunctions.FloatModulo)),
        };
    }
}

/// <summary>The arithmetic MilkDrop's equations use that .NET's Math does differently (or not at all).</summary>
public static class EelFunctions
{
    private const double CloseEnough = 0.00001;

    public static double Divide(double a, double b) => b == 0 ? 0 : a / b;

    /// <summary>Modulo on whole numbers, as ns-eel2 does it; 0 when dividing by 0.</summary>
    public static double Modulo(double a, double b)
    {
        var divisor = (long)b;
        return divisor == 0 ? 0 : (long)a % divisor;
    }

    public static double FloatModulo(double a, double b) => b == 0 ? 0 : a % b;

    public static double Power(double a, double b)
    {
        var result = Math.Pow(a, b);
        return double.IsNaN(result) ? 0 : result;
    }

    public static double BitOr(double a, double b) => (long)a | (long)b;

    public static double BitAnd(double a, double b) => (long)a & (long)b;

    public static double BitXor(double a, double b) => (long)a ^ (long)b;

    public static double Square(double a) => a * a;

    /// <summary>The square root of the size of the number, so a negative one doesn't break the preset.</summary>
    public static double SquareRoot(double a) => Math.Sqrt(Math.Abs(a));

    public static double InverseSquareRoot(double a) => a == 0 ? 0 : 1.0 / Math.Sqrt(Math.Abs(a));

    public static double Log(double a) => a <= 0 ? 0 : Math.Log(a);

    public static double Log10(double a) => a <= 0 ? 0 : Math.Log10(a);

    public static double Sign(double a) => a > 0 ? 1 : a < 0 ? -1 : 0;

    /// <summary>A random whole number from 0 up to (not including) the one given, as in MilkDrop.</summary>
    public static double Random(double below)
    {
        var range = (int)Math.Min(below, int.MaxValue);
        return range < 1 ? 0 : System.Random.Shared.Next(range);
    }

    public static double Sigmoid(double x, double constraint)
    {
        var t = 1.0 + Math.Exp(-x * constraint);
        return Math.Abs(t) > CloseEnough ? 1.0 / t : 0;
    }

    public static double Above(double a, double b) => a > b ? 1 : 0;

    public static double Below(double a, double b) => a < b ? 1 : 0;

    public static double Equal(double a, double b) => Math.Abs(a - b) < CloseEnough ? 1 : 0;

    public static double LogicalAnd(double a, double b) => Math.Abs(a) > CloseEnough && Math.Abs(b) > CloseEnough ? 1 : 0;

    public static double LogicalOr(double a, double b) => Math.Abs(a) > CloseEnough || Math.Abs(b) > CloseEnough ? 1 : 0;

    public static double LogicalNot(double a) => Math.Abs(a) > CloseEnough ? 0 : 1;

    public static int LoopCount(double count, int limit) => count < 1 ? 0 : count > limit ? limit : (int)count;
}
