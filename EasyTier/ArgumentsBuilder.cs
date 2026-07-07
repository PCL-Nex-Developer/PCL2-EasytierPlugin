using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PCL.EasyTierPlugin.EasyTier;

internal sealed class ArgumentsBuilder
{
    private readonly List<Argument> _args = [];

    public ArgumentsBuilder Add(string key, string value)
    {
        if (key is null) throw new NullReferenceException(nameof(key));
        if (value is null) throw new NullReferenceException(nameof(value));
        _args.Add(new Argument(key, HandleValue(value), ArgumentStyle.Equals));
        return this;
    }

    public ArgumentsBuilder AddWithSpace(string key, string value)
    {
        if (key is null) throw new NullReferenceException(nameof(key));
        if (value is null) throw new NullReferenceException(nameof(value));
        _args.Add(new Argument(key, HandleValue(value), ArgumentStyle.Space));
        return this;
    }

    public ArgumentsBuilder AddFlag(string flag)
    {
        if (flag is null) throw new NullReferenceException(nameof(flag));
        _args.Add(new Argument(flag, null, ArgumentStyle.Flag));
        return this;
    }

    public ArgumentsBuilder AddFlagIf(bool condition, string flag)
    {
        if (condition) AddFlag(flag);
        return this;
    }

    public string GetResult()
    {
        var builder = new StringBuilder();

        foreach (var arg in _args)
        {
            if (builder.Length > 0) builder.Append(' ');

            builder.Append(arg.Key.Length == 1 ? '-' : "--").Append(arg.Key);
            if (arg.Value is null) continue;

            if (arg.Style == ArgumentStyle.Equals)
            {
                builder.Append('=').Append(arg.Value);
            }
            else
            {
                builder.Append(' ').Append(arg.Value);
            }
        }

        return builder.ToString();
    }

    private static readonly char[] CharsNeedQuote = [' ', '=', '|', '"'];

    private static string HandleValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return $"\"{value}\"";
        return value.All(x => !CharsNeedQuote.Contains(x))
            ? value
            : $"\"{value.Replace("\"", "\\\"")}\"";
    }

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
}
