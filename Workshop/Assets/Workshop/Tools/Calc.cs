using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OpusSystems.Workshop
{
    /// <summary>
    /// A small exact-arithmetic expression evaluator: + − × ÷ ^, parentheses,
    /// unary minus, postfix % (a percentage: 17% = 0.17), √ or sqrt().
    /// Decimal arithmetic, so 0.1 + 0.2 is 0.3. Throws with a short message
    /// on anything it can't read.
    /// </summary>
    public static class Calc
    {
        public static decimal Evaluate(string expression)
        {
            var tokens = Tokenize(Normalize(expression));
            var rpn = ToRpn(tokens);
            return Run(rpn);
        }

        /// <summary>A result as the tape shows it: no trailing zeros, thousands unmarked.</summary>
        public static string Format(decimal v)
        {
            var s = v.ToString("0.############", CultureInfo.InvariantCulture);
            return s == "-0" ? "0" : s;
        }

        /// <summary>Spoken and typed forms → operator characters.</summary>
        public static string Normalize(string e) => e
            .Replace("×", "*").Replace("x", "*").Replace("X", "*").Replace("÷", "/").Replace("−", "-").Replace("–", "-")
            .Replace("√", "sqrt").Replace(",", "")
            .Replace(" plus ", "+").Replace(" minus ", "-").Replace(" times ", "*").Replace(" divided by ", "/")
            .Replace(" percent", "%").Replace(" of ", "*").Replace(" to the power of ", "^").Replace(" squared", "^2");

        private enum T { Num, Op, LParen, RParen, Func, Percent }

        private struct Tok
        {
            public T Type; public string Text; public decimal Value;
            public override string ToString() => Text;
        }

        private static List<Tok> Tokenize(string s)
        {
            var out_ = new List<Tok>();
            var i = 0;
            while (i < s.Length)
            {
                var c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (char.IsDigit(c) || c == '.')
                {
                    var start = i;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                    if (!decimal.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        throw new Exception($"bad number '{s.Substring(start, i - start)}'");
                    out_.Add(new Tok { Type = T.Num, Text = s.Substring(start, i - start), Value = v });
                    continue;
                }
                if (char.IsLetter(c))
                {
                    var start = i;
                    while (i < s.Length && char.IsLetter(s[i])) i++;
                    var name = s.Substring(start, i - start).ToLowerInvariant();
                    if (name != "sqrt") throw new Exception($"unknown function '{name}'");
                    out_.Add(new Tok { Type = T.Func, Text = name });
                    continue;
                }
                switch (c)
                {
                    case '+': case '-': case '*': case '/': case '^':
                        // Unary minus: at the start, or after an operator or '('.
                        var unary = c == '-' && (out_.Count == 0 || out_[out_.Count - 1].Type == T.Op || out_[out_.Count - 1].Type == T.LParen);
                        out_.Add(new Tok { Type = T.Op, Text = unary ? "neg" : c.ToString() });
                        break;
                    case '(': out_.Add(new Tok { Type = T.LParen, Text = "(" }); break;
                    case ')': out_.Add(new Tok { Type = T.RParen, Text = ")" }); break;
                    case '%': out_.Add(new Tok { Type = T.Percent, Text = "%" }); break;
                    default: throw new Exception($"unexpected '{c}'");
                }
                i++;
            }
            if (out_.Count == 0) throw new Exception("nothing to calculate");
            return out_;
        }

        private static int Prec(string op) => op switch { "neg" => 4, "^" => 3, "*" => 2, "/" => 2, "+" => 1, "-" => 1, _ => 0 };
        private static bool RightAssoc(string op) => op == "^" || op == "neg";

        private static List<Tok> ToRpn(List<Tok> tokens)
        {
            var output = new List<Tok>();
            var ops = new Stack<Tok>();
            foreach (var t in tokens)
            {
                switch (t.Type)
                {
                    case T.Num: output.Add(t); break;
                    case T.Percent: output.Add(t); break; // postfix, binds tightest
                    case T.Func: ops.Push(t); break;
                    case T.Op:
                        while (ops.Count > 0 && ops.Peek().Type != T.LParen &&
                               (ops.Peek().Type == T.Func || Prec(ops.Peek().Text) > Prec(t.Text) ||
                                (Prec(ops.Peek().Text) == Prec(t.Text) && !RightAssoc(t.Text))))
                            output.Add(ops.Pop());
                        ops.Push(t);
                        break;
                    case T.LParen: ops.Push(t); break;
                    case T.RParen:
                        while (ops.Count > 0 && ops.Peek().Type != T.LParen) output.Add(ops.Pop());
                        if (ops.Count == 0) throw new Exception("unbalanced ')'");
                        ops.Pop();
                        if (ops.Count > 0 && ops.Peek().Type == T.Func) output.Add(ops.Pop());
                        break;
                }
            }
            while (ops.Count > 0)
            {
                var o = ops.Pop();
                if (o.Type == T.LParen) throw new Exception("unbalanced '('");
                output.Add(o);
            }
            return output;
        }

        private static decimal Run(List<Tok> rpn)
        {
            var st = new Stack<decimal>();
            foreach (var t in rpn)
            {
                switch (t.Type)
                {
                    case T.Num: st.Push(t.Value); break;
                    case T.Percent: Need(st, 1); st.Push(st.Pop() / 100m); break;
                    case T.Func: Need(st, 1); st.Push(Sqrt(st.Pop())); break;
                    case T.Op:
                        if (t.Text == "neg") { Need(st, 1); st.Push(-st.Pop()); break; }
                        Need(st, 2);
                        var b = st.Pop(); var a = st.Pop();
                        st.Push(t.Text switch
                        {
                            "+" => a + b,
                            "-" => a - b,
                            "*" => a * b,
                            "/" => b == 0 ? throw new Exception("division by zero") : a / b,
                            "^" => Pow(a, b),
                            _ => throw new Exception($"bad operator {t.Text}"),
                        });
                        break;
                }
            }
            if (st.Count != 1) throw new Exception("incomplete expression");
            return st.Pop();
        }

        private static void Need(Stack<decimal> st, int n)
        {
            if (st.Count < n) throw new Exception("incomplete expression");
        }

        private static decimal Pow(decimal a, decimal b)
        {
            if (b == decimal.Truncate(b) && Math.Abs(b) <= 64)
            {
                var r = 1m;
                var n = (int)Math.Abs(b);
                for (var i = 0; i < n; i++) r *= a;
                return b < 0 ? 1m / r : r;
            }
            return (decimal)Math.Pow((double)a, (double)b);
        }

        private static decimal Sqrt(decimal v)
        {
            if (v < 0) throw new Exception("square root of a negative");
            return (decimal)Math.Sqrt((double)v);
        }
    }
}
