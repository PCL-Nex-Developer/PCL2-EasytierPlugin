using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PCL.Core.Utils;

public class ArgumentsBuilder
{
    private readonly List<Argument> _args = [];

    private enum ArgumentStyle
    {
        Flag,
        Equals,
        Space
    }

    private readonly struct Argument(string key, string? value, ArgumentStyle style)
    {
        public readonly string Key = key ?? throw new ArgumentNullException(nameof(key));
        public readonly string? Value = value;
        public readonly ArgumentStyle Style = style;
    }

    public ArgumentsBuilder Add(string key, string value)
    {
        if (key is null) throw new NullReferenceException(nameof(key));
        if (value is null) throw new NullReferenceException(nameof(value));
        _args.Add(new Argument(key, _HandleValue(value), ArgumentStyle.Equals));
        return this;
    }

    public ArgumentsBuilder AddWithSpace(string key, string value)
    {
        if (key is null) throw new NullReferenceException(nameof(key));
        if (value is null) throw new NullReferenceException(nameof(value));
        _args.Add(new Argument(key, _HandleValue(value), ArgumentStyle.Space));
        return this;
    }

    public ArgumentsBuilder AddFlag(string flag)
    {
        if (flag is null) throw new NullReferenceException(nameof(flag));
        _args.Add(new Argument(flag, null, ArgumentStyle.Flag));
        return this;
    }

    public ArgumentsBuilder AddIf(bool condition, string key, string value)
    {
        if (condition) Add(key, value);
        return this;
    }

    public ArgumentsBuilder AddWithSpaceIf(bool condition, string key, string value)
    {
        if (condition) AddWithSpace(key, value);
        return this;
    }

    public ArgumentsBuilder AddFlagIf(bool condition, string flag)
    {
        if (condition) AddFlag(flag);
        return this;
    }

    public enum PrefixStyle
    {
        Auto,
        SingleLine,
        DoubleLine
    }

    public string GetResult(PrefixStyle prefixStyle = 0)
    {
        var sb = new StringBuilder();

        foreach (var arg in _args)
        {
            if (sb.Length > 0) sb.Append(' ');

            switch (prefixStyle)
            {
                case PrefixStyle.SingleLine:
                    sb.Append('-').Append(arg.Key);
                    break;
                case PrefixStyle.DoubleLine:
                    sb.Append("--").Append(arg.Key);
                    break;
                default:
                    sb.Append(arg.Key.Length == 1 ? "-" : "--").Append(arg.Key);
                    break;
            }

            if (arg.Value is not null)
            {
                switch (arg.Style)
                {
                    case ArgumentStyle.Equals:
                        sb.Append('=').Append(arg.Value);
                        break;
                    case ArgumentStyle.Space:
                        sb.Append(' ').Append(arg.Value);
                        break;
                }
            }
        }

        return sb.ToString();
    }

    public override string ToString() => GetResult();

    public void Clear() => _args.Clear();

    private static readonly char[] _CharNeedToQute = [' ', '=', '|', '"'];

    private static string _HandleValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return $"\"{value}\"";
        return value.All(x => !_CharNeedToQute.Contains(x))
            ? value
            : $"\"{value.Replace("\"", "\\\"")}\"";
    }
}